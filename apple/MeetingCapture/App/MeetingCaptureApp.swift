import SwiftUI

@main
public struct MeetingCaptureApp: App {
    public init() {}
    
    public var body: some Scene {
        #if os(macOS)
        WindowGroup {
            MacMainWindowView(vm: MainViewModel())
        }
        .windowStyle(.hiddenTitleBar)
        .windowResizability(.contentSize)
        #elseif os(iOS)
        WindowGroup {
            iOSMainView()
        }
        #endif
    }
}
