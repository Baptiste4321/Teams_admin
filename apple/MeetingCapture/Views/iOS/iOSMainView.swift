import SwiftUI

/// Interface tactile principale pour iOS (iPhone & iPad)
public struct iOSMainView: View {
    @StateObject private var vm = MainViewModel()
    @State private var selectedTab = 0
    
    public init() {}
    
    public var body: some View {
        TabView(selection: $selectedTab) {
            // Onglet 1 : Enregistrement
            NavigationView {
                VStack(spacing: 20) {
                    // Carte Header
                    VStack(spacing: 6) {
                        HStack {
                            Circle()
                                .fill(Color.indigo)
                                .frame(width: 10, height: 10)
                            Text("MeetingCapture Mobile")
                                .font(.headline.bold())
                                .foregroundColor(.white)
                        }
                        Text("Enregistrement de réunion ReplayKit & Whisper")
                            .font(.caption)
                            .foregroundColor(.secondary)
                    }
                    .padding(.top)
                    
                    // Input Titre
                    VStack(alignment: .leading, spacing: 6) {
                        Text("Titre de la réunion")
                            .font(.caption.bold())
                            .foregroundColor(.secondary)
                        TextField("Nom de la réunion (facultatif)...", text: $vm.meetingTitle)
                            .textFieldStyle(.roundedBorder)
                            .disabled(vm.isRecording)
                    }
                    .padding(.horizontal)
                    
                    // Grand bouton REC iOS
                    Button(action: {
                        Task { await vm.toggleRecord() }
                    }) {
                        VStack(spacing: 8) {
                            Circle()
                                .fill(vm.isRecording ? Color.red.opacity(0.8) : Color.red)
                                .frame(width: 80, height: 80)
                                .overlay(
                                    Image(systemName: vm.isRecording ? "stop.fill" : "record.circle")
                                        .font(.system(size: 34))
                                        .foregroundColor(.white)
                                )
                                .shadow(color: .red.opacity(0.4), radius: 10)
                            
                            Text(vm.isRecording ? "ARRÊTER L'ENREGISTREMENT" : "DÉMARRER LA CAPTURE")
                                .font(.system(size: 13, weight: .bold))
                                .foregroundColor(.white)
                        }
                    }
                    .padding(.vertical, 20)
                    
                    // Chrono & Statut
                    VStack(spacing: 4) {
                        Text(vm.elapsedTimeText)
                            .font(.system(size: 36, weight: .bold, design: .monospaced))
                            .foregroundColor(.white)
                        
                        Text(vm.statusMessage)
                            .font(.subheadline)
                            .foregroundColor(.secondary)
                    }
                    
                    // VU-Mètres Audio
                    VStack(spacing: 8) {
                        VuMeterView(label: "AUDIO RÉUNION", level: vm.audioService.systemAudioLevel, color: .green)
                        VuMeterView(label: "MICROPHONE", level: vm.audioService.micAudioLevel, color: .cyan)
                    }
                    .padding()
                    .background(Color(white: 0.12))
                    .cornerRadius(12)
                    .padding(.horizontal)
                    
                    Spacer()
                }
                .navigationTitle("Capture")
                .navigationBarTitleDisplayMode(.inline)
                .background(Color(white: 0.05).ignoresSafeArea())
            }
            .tabItem {
                Label("Enregistrer", systemImage: "record.circle")
            }
            .tag(0)
            
            // Onglet 2 : Historique
            NavigationView {
                iOSHistoryView(vm: HistoryViewModel())
            }
            .tabItem {
                Label("Historique", systemImage: "clock.arrow.circlepath")
            }
            .tag(1)
            
            // Onglet 3 : Réglages
            NavigationView {
                iOSSettingsView(vm: SettingsViewModel())
            }
            .tabItem {
                Label("Réglages", systemImage: "gearshape")
            }
            .tag(2)
        }
        .accentColor(.indigo)
    }
}
