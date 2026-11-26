import XCTest
@testable import RadioClassicCore

final class IcyParserTests: XCTestCase {
    private func icyBlock(_ title: String) -> Data {
        let text = "StreamTitle='\(title)';StreamUrl='';"
        let payload = Data(text.utf8)
        let blocks = Int(ceil(Double(payload.count) / 16.0))
        var padded = payload
        while padded.count < blocks * 16 { padded.append(0) }
        var result = Data([UInt8(blocks)])
        result.append(padded)
        return result
    }

    func testParserExtractsInterleavedMetadataAndStripsItFromAudio() {
        let metaInt = 16
        var stream = Data((0..<metaInt).map { UInt8($0) })
        stream.append(icyBlock("Queen - Bohemian Rhapsody"))
        stream.append(Data(repeating: 99, count: 8))

        var parser = IcyMetadataParser(metaInt: metaInt)
        let events = parser.append(stream)
        var audio = Data()
        var metadata = StreamMetadata.empty
        for event in events {
            switch event {
            case .audio(let chunk): audio.append(chunk)
            case .metadata(let value): metadata = value
            }
        }

        XCTAssertEqual(Data((0..<metaInt).map { UInt8($0) }) + Data(repeating: 99, count: 8), audio)
        XCTAssertEqual("Queen", metadata.artist)
        XCTAssertEqual("Bohemian Rhapsody", metadata.title)
    }

    func testParserHandlesMetadataAcrossTinyChunks() {
        let metaInt = 32
        var stream = Data(repeating: 7, count: metaInt)
        stream.append(icyBlock("A - B"))
        stream.append(Data(repeating: 8, count: metaInt))

        var parser = IcyMetadataParser(metaInt: metaInt)
        var audio = Data()
        var metadata = StreamMetadata.empty
        var offset = 0
        while offset < stream.count {
            let amount = min(3, stream.count - offset)
            let chunk = stream.subdata(in: offset..<(offset + amount))
            for event in parser.append(chunk) {
                switch event {
                case .audio(let bytes): audio.append(bytes)
                case .metadata(let value): metadata = value
                }
            }
            offset += amount
        }

        XCTAssertEqual(Data(repeating: 7, count: metaInt) + Data(repeating: 8, count: metaInt), audio)
        XCTAssertEqual("A", metadata.artist)
        XCTAssertEqual("B", metadata.title)
    }

    func testParserHandlesEmptyMetadataBlock() {
        var stream = Data(repeating: 1, count: 8)
        stream.append(0)
        stream.append(Data(repeating: 2, count: 8))
        var parser = IcyMetadataParser(metaInt: 8)
        let events = parser.append(stream)
        let audio = events.compactMap { event -> Data? in
            if case .audio(let data) = event { return data }
            return nil
        }.reduce(Data(), +)
        XCTAssertEqual(Data(repeating: 1, count: 8) + Data(repeating: 2, count: 8), audio)
        XCTAssertFalse(events.contains { event in
            if case .metadata = event { return true }
            return false
        })
    }

    func testParserWithoutMetaintPassesAllBytes() {
        var parser = IcyMetadataParser(metaInt: 0)
        let input = Data([0, 1, 2, 3, 4])
        let events = parser.append(input)
        XCTAssertEqual(1, events.count)
        if case .audio(let output) = events[0] {
            XCTAssertEqual(input, output)
        } else {
            XCTFail("expected audio event")
        }
    }
}
