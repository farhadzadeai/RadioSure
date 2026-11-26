import Foundation

public final class LocalizationService: ObservableObject {
    public static let shared = LocalizationService()

    public static let languages: [(code: String, name: String)] = [("en", "English"), ("fa", "فارسی")]

    @Published public private(set) var language = "en"
    private var dictionaries: [String: [String: String]] = [:]

    public init(bundle: Bundle? = nil) {
        load(bundle: bundle ?? .module)
    }

    private func load(bundle: Bundle) {
        for (code, _) in Self.languages {
            guard let url = bundle.url(forResource: "strings.\(code)", withExtension: "json"),
                  let data = try? Data(contentsOf: url),
                  let decoded = try? JSONDecoder().decode([String: String].self, from: data) else { continue }
            dictionaries[code] = decoded
        }
    }

    public func setLanguage(_ code: String) {
        language = Self.languages.contains { $0.code == code } ? code : "en"
    }

    public var isRTL: Bool { language == "fa" }
    public var keyCount: Int { dictionaries[language]?.count ?? 0 }

    public func t(_ key: String, _ args: Any...) -> String {
        let template = dictionaries[language]?[key] ?? dictionaries["en"]?[key] ?? key
        var result = template
        for (index, arg) in args.enumerated() {
            result = result.replacingOccurrences(of: "{\(index)}", with: String(describing: arg))
        }
        return result
    }

    public func raw(_ key: String, language: String) -> String? {
        dictionaries[language]?[key]
    }
}
