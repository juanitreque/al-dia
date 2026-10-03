// swift-tools-version: 6.0
import PackageDescription

let package = Package(
    name: "AlDia",
    platforms: [.macOS(.v15)],
    targets: [
        // Lógica fiscal pura (sin UI ni persistencia), testeable.
        .target(name: "AlDiaCore"),
        // App SwiftUI + SwiftData.
        .executableTarget(name: "AlDia", dependencies: ["AlDiaCore"]),
        .testTarget(name: "AlDiaCoreTests", dependencies: ["AlDiaCore"]),
    ],
    swiftLanguageModes: [.v5]
)
