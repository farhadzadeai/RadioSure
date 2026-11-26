import XCTest
@testable import RadioClassicCore

final class ScheduleAndResourceTests: XCTestCase {
    func testDailyOccurrenceComputes() {
        let calendar = Calendar.current
        let today = calendar.startOfDay(for: Date())
        let start = calendar.date(bySettingHour: 14, minute: 30, second: 0, of: today)!
        let schedule = RecordingSchedule(start: start, durationMinutes: 60, repeatMode: .daily)
        let next = schedule.nextStart(after: calendar.date(bySettingHour: 10, minute: 0, second: 0, of: today)!)
        XCTAssertNotNil(next)
        XCTAssertEqual(14, calendar.component(.hour, from: next!))
        XCTAssertEqual(30, calendar.component(.minute, from: next!))
    }

    func testWeeklyRespectsDays() {
        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = TimeZone(secondsFromGMT: 0)!
        let monday = calendar.date(from: DateComponents(year: 2025, month: 3, day: 3, hour: 9))!
        let schedule = RecordingSchedule(
            start: monday.addingTimeInterval(3600),
            durationMinutes: 30,
            repeatMode: .weekly,
            days: (1 << 1) | (1 << 5)
        )
        let wednesday = monday.addingTimeInterval(2 * 86400 + 12 * 3600)
        let next = schedule.nextStart(after: wednesday, calendar: calendar)
        XCTAssertNotNil(next)
        XCTAssertEqual(6, calendar.component(.weekday, from: next!))
    }

    func testDisabledScheduleNeverFires() {
        let schedule = RecordingSchedule(start: Date().addingTimeInterval(3600), repeatMode: .daily, enabled: false)
        XCTAssertNil(schedule.nextStart(after: Date()))
    }

    func testOnceOnlyFiresInFuture() {
        let future = RecordingSchedule(start: Date().addingTimeInterval(7200), repeatMode: .once)
        XCTAssertNotNil(future.nextStart(after: Date()))
        let past = RecordingSchedule(start: Date().addingTimeInterval(-7200), repeatMode: .once)
        XCTAssertNil(past.nextStart(after: Date()))
    }

    func testLocalizationFilesMatchAndContainBothLanguages() throws {
        let service = LocalizationService()
        XCTAssertGreaterThan(service.keyCount, 250)
        service.setLanguage("fa")
        XCTAssertTrue(service.isRTL)
        XCTAssertNotEqual("Search", service.t("Search"))
        XCTAssertTrue(service.t("StationsFound", 12).contains("12"))

        let thisFile = URL(fileURLWithPath: #filePath)
        let root = thisFile.deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
        for language in ["en", "fa"] {
            let shared = try Data(contentsOf: root.appendingPathComponent("Shared/strings.\(language).json"))
            let bundled = try Data(contentsOf: root.appendingPathComponent("Sources/RadioClassicCore/Resources/strings.\(language).json"))
            XCTAssertEqual(shared, bundled, "bundled strings.\(language).json must be an exact copy of Shared")
        }
    }

    func testThemeCatalogHasSixPalettesAndValidColors() {
        let service = ThemeService()
        XCTAssertEqual(6, service.themes.count)
        for name in ThemeService.order {
            XCTAssertNotNil(service.themes[name], "missing \(name)")
        }
        service.setTheme("DarkClassic")
        XCTAssertEqual("DarkClassic", service.currentName)
        XCTAssertFalse(service.current.hex("WindowBack").isEmpty)
        XCTAssertFalse(service.current.hex("AccentColor").isEmpty)
    }
}
