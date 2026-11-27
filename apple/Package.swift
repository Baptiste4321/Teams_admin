// swift-tools-version: 5.9
import PackageDescription

let package = Package(
    name: "MeetingCapture",
    defaultLocalization: "fr",
    platforms: [
        .macOS(.v13),
        .iOS(.v16)
    ],
    products: [
        .executable(
            name: "MeetingCapture",
            targets: ["MeetingCapture"]
        )
    ],
    dependencies: [],
    targets: [
        .executableTarget(
            name: "MeetingCapture",
            path: "MeetingCapture"
        )
    ]
)
