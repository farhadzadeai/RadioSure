// swift-tools-version:5.9
import PackageDescription

let package = Package(
    name: "RadioClassicMac",
    platforms: [.macOS(.v13)],
    products: [
        .library(name: "RadioClassicCore", targets: ["RadioClassicCore"]),
        .executable(name: "RadioClassicMac", targets: ["RadioClassicMac"])
    ],
    targets: [
        .target(
            name: "RadioClassicCore",
            path: "Sources/RadioClassicCore",
            resources: [.process("Resources")]
        ),
        .executableTarget(
            name: "RadioClassicMac",
            dependencies: ["RadioClassicCore"],
            path: "Sources/RadioClassicMac"
        ),
        .testTarget(
            name: "RadioClassicCoreTests",
            dependencies: ["RadioClassicCore"],
            path: "Tests/RadioClassicCoreTests"
        )
    ]
)
