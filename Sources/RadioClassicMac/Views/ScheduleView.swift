import RadioClassicCore
import SwiftUI

struct ScheduleView: View {
    @EnvironmentObject private var model: MainViewModel
    @Environment(\.dismiss) private var dismiss
    let station: Station

    @State private var schedules: [RecordingSchedule] = []
    @State private var editingID: Int64 = 0
    @State private var start = Date().addingTimeInterval(3600)
    @State private var duration = 60
    @State private var repeatMode: ScheduleRepeat = .once
    @State private var days = 0
    @State private var enabled = true
    @State private var errorText = ""
    @State private var confirmDelete: RecordingSchedule?

    var body: some View {
        VStack(spacing: 0) {
            HStack {
                Text(model.localization.t(editingID == 0 ? "Sch.AddTitle" : "Sch.EditTitle"))
                    .font(.system(size: 14, weight: .semibold))
                Spacer()
            }
            .padding(.horizontal, 14)
            .padding(.vertical, 10)

            HStack(alignment: .top, spacing: 12) {
                List(schedules, selection: .constant(nil as Int64?)) { schedule in
                    HStack {
                        VStack(alignment: .leading, spacing: 2) {
                            Text(schedule.stationName)
                                .font(.system(size: 11, weight: .medium))
                                .lineLimit(1)
                            Text(summary(schedule))
                                .font(.system(size: 9))
                                .foregroundStyle(.secondary)
                                .lineLimit(2)
                        }
                        Spacer()
                    }
                    .contentShape(Rectangle())
                    .onTapGesture { load(schedule) }
                    .contextMenu {
                        Button(model.localization.t("Sch.Edit")) { load(schedule) }
                        Button(model.localization.t("Sch.Delete"), role: .destructive) { confirmDelete = schedule }
                    }
                }
                .frame(width: 250)
                .listStyle(.plain)
                .overlay {
                    if schedules.isEmpty {
                        Text(model.localization.t("Rec.NoneScheduled"))
                            .font(.system(size: 10))
                            .foregroundStyle(.secondary)
                    }
                }

                Form {
                    Section {
                        HStack {
                            Text(model.localization.t("Sch.Station"))
                                .frame(width: 84, alignment: .leading)
                            Text(station.name)
                                .lineLimit(1)
                            Spacer()
                        }
                        DatePicker(selection: $start, displayedComponents: [.date, .hourAndMinute]) {
                            Text(model.localization.t("Sch.Time"))
                        }
                        Stepper(value: $duration, in: 1...1440) {
                            HStack {
                                Text(model.localization.t("Sch.Duration"))
                                Spacer()
                                Text("\(duration) min")
                            }
                        }
                        Picker(model.localization.t("Sch.Repeat"), selection: $repeatMode) {
                            Text(model.localization.t("Sch.Once")).tag(ScheduleRepeat.once)
                            Text(model.localization.t("Sch.Daily")).tag(ScheduleRepeat.daily)
                            Text(model.localization.t("Sch.Weekly")).tag(ScheduleRepeat.weekly)
                        }
                        if repeatMode == .weekly { weekdayPicker }
                        Toggle(model.localization.t("Enabled"), isOn: $enabled)
                    }
                    if !errorText.isEmpty {
                        Text(errorText)
                            .font(.system(size: 10))
                            .foregroundStyle(.red)
                    }
                }
                .formStyle(.grouped)
            }
            .padding(.horizontal, 14)

            HStack {
                Button(model.localization.t("Sch.Add")) { reset() }
                Spacer()
                Button(model.localization.t("Cancel")) { dismiss() }
                Button(model.localization.t("Save")) { save() }
                    .keyboardShortcut(.defaultAction)
            }
            .padding(12)
        }
        .frame(width: 650, height: 470)
        .onAppear(perform: reload)
        .alert(item: $confirmDelete) { schedule in
            Alert(
                title: Text(model.localization.t("Sch.DeleteConfirm")),
                primaryButton: .destructive(Text(model.localization.t("Yes"))) {
                    try? model.recordingRepository.deleteSchedule(schedule.id)
                    reset()
                    reload()
                },
                secondaryButton: .cancel(Text(model.localization.t("No")))
            )
        }
    }

    private var weekdayPicker: some View {
        VStack(alignment: .leading, spacing: 5) {
            Text(model.localization.t("Sch.Days"))
            HStack(spacing: 4) {
                ForEach(0..<7, id: \.self) { index in
                    Toggle(weekdayName(index), isOn: dayBinding(index))
                        .toggleStyle(.button)
                        .controlSize(.small)
                }
            }
        }
    }

    private func dayBinding(_ index: Int) -> Binding<Bool> {
        Binding(
            get: { (days & (1 << index)) != 0 },
            set: { enabled in
                if enabled { days |= 1 << index } else { days &= ~(1 << index) }
            }
        )
    }

    private func weekdayName(_ index: Int) -> String {
        let keys = ["Day.Su", "Day.Mo", "Day.Tu", "Day.We", "Day.Th", "Day.Fr", "Day.Sa"]
        return model.localization.t(keys[index])
    }

    private func reload() {
        let all = (try? model.recordingRepository.schedules()) ?? []
        schedules = all.filter { $0.stationID == station.id || $0.stationURL == station.url } + all.filter { $0.stationID != station.id && $0.stationURL != station.url }
    }

    private func load(_ schedule: RecordingSchedule) {
        editingID = schedule.id
        start = schedule.start
        duration = schedule.durationMinutes
        repeatMode = schedule.repeatMode
        days = schedule.days
        enabled = schedule.enabled
        errorText = ""
    }

    private func reset() {
        editingID = 0
        start = Date().addingTimeInterval(3600)
        duration = 60
        repeatMode = .once
        days = 0
        enabled = true
        errorText = ""
    }

    private func save() {
        guard duration > 0 else {
            errorText = model.localization.t("Sch.InvalidDuration")
            return
        }
        if repeatMode == .weekly && days == 0 {
            let weekday = Calendar.current.component(.weekday, from: start) - 1
            days = 1 << weekday
        }
        var schedule = RecordingSchedule(
            id: editingID,
            stationID: station.id,
            stationName: station.name,
            stationURL: station.url,
            start: start,
            durationMinutes: duration,
            repeatMode: repeatMode,
            days: days,
            enabled: enabled
        )
        do {
            try model.recordingRepository.saveSchedule(&schedule)
            reset()
            reload()
        } catch {
            errorText = error.localizedDescription
        }
    }

    private func summary(_ schedule: RecordingSchedule) -> String {
        let mode = switch schedule.repeatMode {
        case .once: model.localization.t("Sch.Once")
        case .daily: model.localization.t("Sch.Daily")
        case .weekly: model.localization.t("Sch.Weekly")
        }
        let next = schedule.nextStart(after: Date())?.formatted(date: .abbreviated, time: .shortened) ?? "—"
        return "\(schedule.start.formatted(date: .abbreviated, time: .shortened)) · \(schedule.durationMinutes)m · \(mode)\n\(model.localization.t("Sch.NextRun")): \(next)"
    }
}
