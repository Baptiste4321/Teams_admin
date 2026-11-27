#if os(macOS)
import SwiftUI

/// Fenêtre de Paramètres macOS
public struct MacSettingsView: View {
    @ObservedObject var vm: SettingsViewModel
    @Environment(\.presentationMode) var presentationMode
    
    public init(vm: SettingsViewModel) {
        self.vm = vm
    }
    
    public var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            HStack {
                Circle()
                    .fill(Color.indigo)
                    .frame(width: 8, height: 8)
                Text("Paramètres de Capture macOS")
                    .font(.headline)
                Spacer()
                Button("✕") {
                    presentationMode.wrappedValue.dismiss()
                }
                .buttonStyle(.plain)
            }
            
            Divider()
            
            // Section Vidéo
            VStack(alignment: .leading, spacing: 8) {
                Text("SOURCE VIDÉO")
                    .font(.system(size: 10, weight: .bold))
                    .foregroundColor(.secondary)
                
                Picker("Cadence (FPS)", selection: $vm.fps) {
                    ForEach(vm.fpsOptions, id: \.self) { fps in
                        Text("\(fps) FPS").tag(fps)
                    }
                }
                
                Toggle("Mode plein écran par défaut", isOn: $vm.isDesktopMode)
            }
            .padding()
            .background(Color(white: 0.1))
            .cornerRadius(8)
            
            // Section Audio Loopback
            VStack(alignment: .leading, spacing: 6) {
                Text("🎧 ScreenCaptureKit Audio Loopback")
                    .font(.system(size: 12, weight: .bold))
                    .foregroundColor(.indigo)
                Text("Sur macOS, l'audio de Microsoft Teams est capturé directement via le flux SCStream de l'application. Aucun bruit système ni haut-parleur physique n'est requis.")
                    .font(.system(size: 11))
                    .foregroundColor(.secondary)
                
                Toggle("Inclure le microphone local", isOn: $vm.isMicEnabled)
                    .padding(.top, 4)
            }
            .padding()
            .background(Color(white: 0.1))
            .cornerRadius(8)
            
            Spacer()
            
            HStack {
                Spacer()
                Button("Enregistrer et Fermer") {
                    presentationMode.wrappedValue.dismiss()
                }
                .buttonStyle(.borderedProminent)
                .tint(.indigo)
            }
        }
        .padding(18)
        .frame(width: 440, height: 380)
    }
}

#endif
