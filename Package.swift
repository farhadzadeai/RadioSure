// swift-tools-version:5.9
import PackageDescription

let package = Package(
    name: "RadioClassicMac",
    platforms: [.macOS(.v13)],
    targets: [
        .executableTarget(
            name: "RadioClassicMac",
            path: "Sources/RadioClassicMac"
        )
    ]
)
