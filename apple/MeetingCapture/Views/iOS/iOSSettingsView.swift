import SwiftUI

/// Vue de Réglages pour iOS
public struct iOSSettingsView: View {
    @ObservedObject var vm: SettingsViewModel
    
    public init(vm: SettingsViewModel) {
        self.vm = vm
    }
    
    public var body: some View {
        Form {
            Section(header: Text("Enregistrement")) {
                Picker("Cadence Vidéo (FPS)", selection: $vm.fps) {
                    ForEach(vm.fpsOptions, id: \.self) { fps in
                        Text("\(fps) FPS").tag(fps)
                    }
                }
                
                Toggle("Inclure le microphone", isOn: $vm.isMicEnabled)
            }
            
            Section(header: Text("ReplayKit")) {
                Text("Sur iOS, la capture utilise ReplayKit pour enregistrer l'écran et le son de la réunion en haute fidélité.")
                    .font(.caption)
                    .foregroundColor(.secondary)
            }
            
            Section(header: Text("À propos")) {
                HStack {
                    Text("Version")
                    Spacer()
                    Text("1.0.0")
                        .foregroundColor(.secondary)
                }
            }
        }
        .navigationTitle("Réglages")
    }
}
