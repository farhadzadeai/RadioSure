import Foundation

public final class StationSyncService {
    private let lock = NSLock()
    private var running = false

    public init() {}

    public var isRunning: Bool {
        lock.lock()
        defer { lock.unlock() }
        return running
    }

    private func beginIfPossible() -> Bool {
        lock.lock()
        defer { lock.unlock() }
        if running { return false }
        running = true
        return true
    }

    private func finishRun() {
        lock.lock()
        running = false
        lock.unlock()
    }

    public func isRefreshDue(repository: StationRepository, settings: AppSettings) -> Bool {
        let count = (try? repository.count()) ?? 0
        guard count > 0, let last = settings.lastDatabaseSync else { return true }
        return Date().timeIntervalSince(last) > TimeInterval(settings.databaseRefreshDays * 86400)
    }

    @discardableResult
    public func refresh(
        client: RadioBrowserClient,
        repository: StationRepository,
        progress: @escaping (Int) -> Void = { _ in }
    ) async throws -> Int {
        guard beginIfPossible() else { return 0 }
        defer { finishRun() }

        let generation = Int64(Date().timeIntervalSince1970)
        var received = 0
        var batch: [Station] = []
        batch.reserveCapacity(2000)

        for try await station in client.allStations() {
            batch.append(station)
            if batch.count >= 2000 {
                try repository.upsertRemote(batch, generation: generation)
                received += batch.count
                progress(received)
                batch.removeAll(keepingCapacity: true)
            }
        }
        if !batch.isEmpty {
            try repository.upsertRemote(batch, generation: generation)
            received += batch.count
            progress(received)
        }
        guard received >= 1000 else {
            throw RadioBrowserError("The station list received was incomplete.")
        }
        if let expected = client.expectedWorkingStationCount, expected > 1000 {
            let minimum = Int(Double(expected) * 0.85)
            guard received >= minimum else {
                throw RadioBrowserError("The station list received was incomplete.")
            }
        }
        try repository.finishSync(generation: generation)
        return received
    }
}
