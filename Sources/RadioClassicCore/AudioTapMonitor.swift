import AVFoundation
import MediaToolbox
import QuartzCore

private func audioTapInitCallback(tap: MTAudioProcessingTap, clientInfo: UnsafeMutableRawPointer?, tapStorageOut: UnsafeMutablePointer<UnsafeMutableRawPointer?>) {
    tapStorageOut.pointee = clientInfo
}

private func audioTapFinalizeCallback(tap: MTAudioProcessingTap) {
    let storage = MTAudioProcessingTapGetStorage(tap)
    Unmanaged<AudioTapMonitor>.fromOpaque(storage).release()
}

private func audioTapPrepareCallback(tap: MTAudioProcessingTap, maxFrames: CMItemCount, processingFormat: UnsafePointer<AudioStreamBasicDescription>) {
    let storage = MTAudioProcessingTapGetStorage(tap)
    let monitor = Unmanaged<AudioTapMonitor>.fromOpaque(storage).takeUnretainedValue()
    monitor.configure(format: processingFormat.pointee)
}

private func audioTapUnprepareCallback(tap: MTAudioProcessingTap) {}

private func audioTapProcessCallback(
    tap: MTAudioProcessingTap,
    numberFrames: CMItemCount,
    flags: MTAudioProcessingTapFlags,
    bufferListInOut: UnsafeMutablePointer<AudioBufferList>,
    numberFramesOut: UnsafeMutablePointer<CMItemCount>,
    flagsOut: UnsafeMutablePointer<MTAudioProcessingTapFlags>
) {
    var sourceFlags: MTAudioProcessingTapFlags = 0
    var frames: CMItemCount = 0
    let status = MTAudioProcessingTapGetSourceAudio(tap, numberFrames, bufferListInOut, &sourceFlags, nil, &frames)
    guard status == noErr else {
        numberFramesOut.pointee = 0
        flagsOut.pointee = 0
        return
    }
    numberFramesOut.pointee = frames
    flagsOut.pointee = sourceFlags
    let storage = MTAudioProcessingTapGetStorage(tap)
    let monitor = Unmanaged<AudioTapMonitor>.fromOpaque(storage).takeUnretainedValue()
    monitor.ingest(bufferList: bufferListInOut, frameCount: Int(frames))
}

private struct Biquad {
    var b0 = 0.0 as Float
    var b1 = 0.0 as Float
    var b2 = 0.0 as Float
    var a1 = 0.0 as Float
    var a2 = 0.0 as Float
    var z1 = 0.0 as Float
    var z2 = 0.0 as Float

    mutating func process(_ input: Float) -> Float {
        let output = b0 * input + z1
        z1 = b1 * input - a1 * output + z2
        z2 = b2 * input - a2 * output
        return output
    }

    static func peaking(frequency: Double, gainDB: Double, sampleRate: Double, q: Double = 0.9) -> Biquad {
        guard frequency > 0, frequency < sampleRate * 0.45, sampleRate > 0 else { return Biquad() }
        let amplitude = pow(10.0, gainDB / 40.0)
        let omega = 2.0 * Double.pi * frequency / sampleRate
        let sine = sin(omega)
        let cosine = cos(omega)
        let alpha = sine / (2.0 * q)
        let denominator = 1.0 + alpha / amplitude
        var filter = Biquad()
        filter.b0 = Float((1.0 + alpha * amplitude) / denominator)
        filter.b1 = Float((-2.0 * cosine) / denominator)
        filter.b2 = Float((1.0 - alpha * amplitude) / denominator)
        filter.a1 = Float((-2.0 * cosine) / denominator)
        filter.a2 = Float((1.0 - alpha / amplitude) / denominator)
        return filter
    }
}

public final class AudioTapMonitor {
    private let lock = NSLock()
    private var tapFormat = AudioStreamBasicDescription()
    private var formatSupported = false
    private var peak: Float = 0
    private var lastProcessHostTime: CFTimeInterval = 0
    private var lastNonSilentHostTime: CFTimeInterval
    private var ring = [Float](repeating: 0, count: 2048)
    private var writeIndex = 0
    private var coefficientSampleRate = 0.0
    private var coefficientBins = 0
    private var dftCos: [[Float]] = []
    private var dftSin: [[Float]] = []
    private let dftSize = 512
    private var equalizer: EqSettings
    private var preampGain: Float = 1
    private var channelGain: [Float] = [1, 1]
    private var toneFilters: [[Biquad]] = []
    private var bandFilters: [[Biquad]] = []

    public private(set) var tap: MTAudioProcessingTap?
    public private(set) var creationStatus: OSStatus = noErr

    public init(equalizer: EqSettings = EqSettings()) {
        self.equalizer = equalizer
        let now = CACurrentMediaTime()
        lastNonSilentHostTime = now
        let clientInfo = UnsafeMutableRawPointer(Unmanaged.passRetained(self).toOpaque())
        var callbacks = MTAudioProcessingTapCallbacks(
            version: kMTAudioProcessingTapCallbacksVersion_0,
            clientInfo: clientInfo,
            init: audioTapInitCallback,
            finalize: audioTapFinalizeCallback,
            prepare: audioTapPrepareCallback,
            unprepare: audioTapUnprepareCallback,
            process: audioTapProcessCallback
        )
        var created: MTAudioProcessingTap?
        creationStatus = MTAudioProcessingTapCreate(
            kCFAllocatorDefault,
            &callbacks,
            kMTAudioProcessingTapCreationFlag_PostEffects,
            &created
        )
        if creationStatus != noErr {
            Unmanaged<AudioTapMonitor>.fromOpaque(clientInfo).release()
        }
        tap = created
    }

    public func invalidate() {
        tap = nil
    }

    public func updateEqualizer(_ settings: EqSettings) {
        lock.lock()
        if equalizer != settings {
            equalizer = settings
            prepareEqualizerLocked()
        }
        lock.unlock()
    }

    public func resetSilenceMeasurement() {
        lock.lock()
        lastNonSilentHostTime = CACurrentMediaTime()
        lock.unlock()
    }

    public func makeAudioMix() -> AVAudioMix? {
        guard let tap else { return nil }
        let parameters = AVMutableAudioMixInputParameters()
        parameters.audioTapProcessor = tap
        let mix = AVMutableAudioMix()
        mix.inputParameters = [parameters]
        return mix
    }

    fileprivate func configure(format: AudioStreamBasicDescription) {
        lock.lock()
        tapFormat = format
        let isPCM = format.mFormatID == kAudioFormatLinearPCM
        let isFloat = (format.mFormatFlags & kAudioFormatFlagIsFloat) != 0
        let isSigned = (format.mFormatFlags & kAudioFormatFlagIsSignedInteger) != 0
        formatSupported = isPCM && (isFloat || isSigned)
        coefficientSampleRate = 0
        prepareEqualizerLocked()
        lock.unlock()
    }

    fileprivate func ingest(bufferList: UnsafeMutablePointer<AudioBufferList>, frameCount: Int) {
        guard frameCount > 0 else { return }
        lock.lock()
        defer { lock.unlock() }
        let now = CACurrentMediaTime()
        lastProcessHostTime = now
        guard formatSupported else { return }

        let buffers = UnsafeMutableAudioBufferListPointer(bufferList)
        var maxPeak: Float = 0
        let channels = max(1, Int(tapFormat.mChannelsPerFrame))
        let nonInterleaved = (tapFormat.mFormatFlags & kAudioFormatFlagIsNonInterleaved) != 0
        let isFloat = (tapFormat.mFormatFlags & kAudioFormatFlagIsFloat) != 0
        let bytesPerSample = max(1, Int(tapFormat.mBitsPerChannel / 8))

        for (bufferIndexOffset, buffer) in buffers.enumerated() {
            guard let data = buffer.mData else { continue }
            let availableSamples = Int(buffer.mDataByteSize) / bytesPerSample
            let samplesInThisBuffer = nonInterleaved ? min(frameCount, availableSamples) : min(frameCount * channels, availableSamples)
            if isFloat && bytesPerSample == 4 {
                let samples = data.assumingMemoryBound(to: Float.self)
                for index in 0..<samplesInThisBuffer {
                    let channel = nonInterleaved ? bufferIndexOffset : index % channels
                    let value = processEqualizerLocked(samples[index], channel: channel)
                    samples[index] = value
                    let magnitude = abs(value)
                    if magnitude > maxPeak { maxPeak = magnitude }
                    pushSample(value)
                }
            } else if bytesPerSample == 2 {
                let samples = data.assumingMemoryBound(to: Int16.self)
                for index in 0..<samplesInThisBuffer {
                    let channel = nonInterleaved ? bufferIndexOffset : index % channels
                    let value = processEqualizerLocked(Float(samples[index]) / Float(Int16.max), channel: channel)
                    samples[index] = Int16(max(-1, min(1, value)) * Float(Int16.max))
                    let magnitude = abs(value)
                    if magnitude > maxPeak { maxPeak = magnitude }
                    pushSample(value)
                }
            }
        }
        peak = maxPeak
        if maxPeak > 0.0001 {
            lastNonSilentHostTime = now
        }
    }

    private func pushSample(_ sample: Float) {
        ring[writeIndex] = sample
        writeIndex = (writeIndex + 1) % ring.count
    }

    private func prepareEqualizerLocked() {
        let sampleRate = Double(tapFormat.mSampleRate)
        let channels = max(1, Int(tapFormat.mChannelsPerFrame))
        preampGain = equalizer.enabled ? Float(pow(10.0, equalizer.preamp / 20.0)) : 1
        let balance = max(-1.0, min(1.0, equalizer.balance))
        channelGain = Array(repeating: 1, count: channels)
        if channels >= 2 {
            channelGain[0] = Float(balance > 0 ? 1 - balance : 1)
            channelGain[1] = Float(balance < 0 ? 1 + balance : 1)
        }
        toneFilters = []
        bandFilters = []
        guard equalizer.enabled, sampleRate > 8000 else { return }
        for _ in 0..<channels {
            var tones: [Biquad] = []
            if equalizer.bass != 0 {
                tones.append(Biquad.peaking(frequency: 140, gainDB: equalizer.bass, sampleRate: sampleRate, q: 0.75))
            }
            if equalizer.treble != 0 {
                tones.append(Biquad.peaking(frequency: min(6500, sampleRate * 0.28), gainDB: equalizer.treble, sampleRate: sampleRate, q: 0.75))
            }
            toneFilters.append(tones)
            let frequencies = [31.0, 62.0, 125.0, 250.0, 500.0, 1000.0, 2000.0, 4000.0, 8000.0, 16000.0]
            var bands: [Biquad] = []
            for (index, gain) in equalizer.bands.enumerated() where index < frequencies.count && gain != 0 {
                let frequency = frequencies[index]
                if frequency < sampleRate * 0.42 {
                    bands.append(Biquad.peaking(frequency: frequency, gainDB: gain, sampleRate: sampleRate, q: 1.05))
                }
            }
            bandFilters.append(bands)
        }
    }

    private func processEqualizerLocked(_ input: Float, channel: Int) -> Float {
        guard equalizer.enabled else { return input }
        var value = input * preampGain
        if toneFilters.indices.contains(channel) {
            for index in toneFilters[channel].indices {
                value = toneFilters[channel][index].process(value)
            }
        }
        if bandFilters.indices.contains(channel) {
            for index in bandFilters[channel].indices {
                value = bandFilters[channel][index].process(value)
            }
        }
        let gain = channelGain.indices.contains(channel) ? channelGain[channel] : 1
        return max(-1.5, min(1.5, value * gain))
    }

    public var audioLevel: Float {
        lock.lock()
        defer { lock.unlock() }
        return peak
    }

    public var isProducingAudio: Bool {
        lock.lock()
        defer { lock.unlock() }
        guard formatSupported, lastProcessHostTime > 0 else { return false }
        return CACurrentMediaTime() - lastProcessHostTime < 1.5
    }

    public var continuousSilenceDuration: TimeInterval {
        lock.lock()
        defer { lock.unlock() }
        guard formatSupported, lastProcessHostTime > 0 else { return 0 }
        return max(0, CACurrentMediaTime() - lastNonSilentHostTime)
    }

    public func spectrum(bins: Int) -> [Float] {
        lock.lock()
        defer { lock.unlock() }
        guard bins > 0, formatSupported else { return Array(repeating: 0, count: max(0, bins)) }
        prepareCoefficientsLocked(bins: bins)
        guard !dftCos.isEmpty else { return Array(repeating: 0, count: bins) }

        var samples = [Float](repeating: 0, count: dftSize)
        for index in 0..<dftSize {
            let ringIndex = (writeIndex - dftSize + index + ring.count * 2) % ring.count
            samples[index] = ring[ringIndex]
        }
        let twoPi = Float.pi * 2
        for index in 0..<dftSize {
            let window = 0.5 * (1 - cos(twoPi * Float(index) / Float(dftSize - 1)))
            samples[index] *= window
        }

        var output = [Float](repeating: 0, count: bins)
        for band in 0..<bins {
            var real: Float = 0
            var imaginary: Float = 0
            for index in 0..<dftSize {
                real += samples[index] * dftCos[band][index]
                imaginary -= samples[index] * dftSin[band][index]
            }
            let magnitude = sqrt(real * real + imaginary * imaginary) / Float(dftSize / 4)
            output[band] = min(1, log10(1 + magnitude * 25))
        }
        return output
    }

    private func prepareCoefficientsLocked(bins: Int) {
        let sampleRate = Double(tapFormat.mSampleRate)
        guard coefficientSampleRate != sampleRate || coefficientBins != bins || dftCos.count != bins else { return }
        coefficientSampleRate = sampleRate
        coefficientBins = bins
        dftCos = []
        dftSin = []
        let nyquist = max(8000.0, sampleRate / 2)
        for band in 0..<bins {
            let position = Double(band) / Double(max(1, bins - 1))
            let frequency = 30 * pow(nyquist / 30, position)
            let harmonic = max(1, Int((frequency * Double(dftSize) / sampleRate).rounded()))
            var cosRow = [Float](repeating: 0, count: dftSize)
            var sinRow = [Float](repeating: 0, count: dftSize)
            for index in 0..<dftSize {
                let angle = 2 * Double.pi * Double(harmonic * index) / Double(dftSize)
                cosRow[index] = Float(cos(angle))
                sinRow[index] = Float(sin(angle))
            }
            dftCos.append(cosRow)
            dftSin.append(sinRow)
        }
    }
}
