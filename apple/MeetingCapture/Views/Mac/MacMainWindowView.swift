#if os(macOS)
import SwiftUI

/// Widget principal flottant macOS Studio
public struct MacMainWindowView: View {
    @ObservedObject var vm: MainViewModel
    
    public init(vm: MainViewModel) {
        self.vm = vm
    }
    
    public var body: some View {
        VStack(spacing: 10) {
            // Header Bar
            HStack {
                Circle()
                    .fill(Color.indigo)
                    .frame(width: 8, height: 8)
                    .shadow(color: .indigo, radius: 4)
                
                Text("MeetingCapture")
                    .font(.system(size: 13, weight: .bold))
                    .foregroundColor(.white)
                
                Text("STUDIO")
                    .font(.system(size: 9, weight: .bold))
                    .foregroundColor(Color(red: 0.5, green: 0.55, blue: 0.98))
                    .padding(.horizontal, 5)
                    .padding(.vertical, 1)
                    .background(Color(white: 0.15))
                    .cornerRadius(4)
                
                Spacer()
                
                // Pin Button
                Button(action: { vm.isAlwaysOnTop.toggle() }) {
                    Text("📌")
                        .font(.system(size: 12))
                        .foregroundColor(vm.isAlwaysOnTop ? .indigo : .gray)
                }
                .buttonStyle(.plain)
                
                // Settings
                Button(action: { vm.showSettings.toggle() }) {
                    Text("⚙")
                        .font(.system(size: 13))
                        .foregroundColor(.gray)
                }
                .buttonStyle(.plain)
                
                // History
                Button(action: { vm.showHistory.toggle() }) {
                    Text("📂")
                        .font(.system(size: 13))
                        .foregroundColor(.gray)
                }
                .buttonStyle(.plain)
            }
            .padding(.bottom, 2)
            
            // Input Titre Réunion
            HStack {
                Text("✏️ Titre")
                    .font(.system(size: 11, weight: .semibold))
                    .foregroundColor(.secondary)
                    .frame(width: 50, alignment: .leading)
                
                TextField("Nom de la réunion (facultatif)...", text: $vm.meetingTitle)
                    .textFieldStyle(.plain)
                    .padding(6)
                    .background(Color(white: 0.12))
                    .cornerRadius(6)
                    .disabled(vm.isRecording)
            }
            .padding(8)
            .background(Color(white: 0.08))
            .cornerRadius(8)
            
            // Sélecteur Cible (ScreenCaptureKit)
            HStack {
                Text("🎯 Cible")
                    .font(.system(size: 11, weight: .semibold))
                    .foregroundColor(.secondary)
                    .frame(width: 50, alignment: .leading)
                
                Picker("", selection: $vm.selectedWindow) {
                    Text("Écran entier / Bureau").tag(nil as WindowInfo?)
                    ForEach(vm.captureService.availableWindows) { win in
                        Text("[\(win.processName)] \(win.title)").tag(win as WindowInfo?)
                    }
                }
                .pickerStyle(.menu)
                .labelsHidden()
                
                Button(action: { Task { await vm.refreshWindows() } }) {
                    Text("🔄")
                        .font(.system(size: 11))
                }
                .buttonStyle(.plain)
                .padding(4)
                .background(Color(white: 0.15))
                .cornerRadius(4)
            }
            .padding(8)
            .background(Color(white: 0.08))
            .cornerRadius(8)
            
            // Barre Principale : Bouton REC Studio + Chrono
            HStack(spacing: 12) {
                // Bouton REC
                Button(action: { Task { await vm.toggleRecord() } }) {
                    HStack(spacing: 8) {
                        Circle()
                            .fill(Color.red)
                            .frame(width: 12, height: 12)
                            .scaleEffect(vm.isRecording ? (vm.pulseRec ? 1.25 : 0.85) : 1.0)
                            .animation(vm.isRecording ? .easeInOut(duration: 0.75).repeatForever(autoreverses: true) : .default, value: vm.pulseRec)
                        
                        Text(vm.isRecording ? "STOP" : "REC")
                            .font(.system(size: 13, weight: .bold))
                            .foregroundColor(.white)
                    }
                    .frame(width: 105, height: 38)
                    .background(vm.isRecording ? Color(red: 0.2, green: 0.1, blue: 0.1) : Color.red)
                    .cornerRadius(8)
                    .overlay(
                        RoundedRectangle(cornerRadius: 8)
                            .stroke(vm.isRecording ? Color.red : Color.clear, lineWidth: 1.5)
                    )
                }
                .buttonStyle(.plain)
                .onAppear { vm.pulseRec = true }
                
                // Chrono & Statut
                VStack(alignment: .leading, spacing: 2) {
                    HStack {
                        Text(vm.elapsedTimeText)
                            .font(.system(size: 17, weight: .bold, design: .monospaced))
                            .foregroundColor(.white)
                        
                        if vm.isRecording {
                            Text("LIVE")
                                .font(.system(size: 9, weight: .bold))
                                .foregroundColor(.red)
                                .padding(.horizontal, 5)
                                .padding(.vertical, 1)
                                .background(Color(red: 0.3, green: 0.05, blue: 0.05))
                                .cornerRadius(3)
                        }
                    }
                    
                    Text(vm.statusMessage)
                        .font(.system(size: 10))
                        .foregroundColor(.secondary)
                        .lineLimit(1)
                }
                
                Spacer()
                
                // Bouton tiroir transcription
                Button(action: { vm.isDetailsExpanded.toggle() }) {
                    Image(systemName: vm.isDetailsExpanded ? "chevron.up" : "chevron.down")
                        .font(.system(size: 11))
                        .foregroundColor(.secondary)
                        .padding(6)
                        .background(Color(white: 0.14))
                        .cornerRadius(6)
                }
                .buttonStyle(.plain)
            }
            .padding(10)
            .background(Color(white: 0.08))
            .cornerRadius(10)
            
            // Console Audio VU-Meters & Badges
            VStack(spacing: 8) {
                HStack {
                    StatusBadgeView(icon: "🎥", text: "\(Int(vm.captureService.currentFps)) FPS", color: .blue)
                    StatusBadgeView(icon: "🎧", text: "ISOLÉ", color: .green)
                    StatusBadgeView(icon: "🎙️", text: "WHISPER", color: .orange)
                    StatusBadgeView(icon: "💬", text: "CHAT", color: .purple)
                }
                
                HStack(spacing: 12) {
                    VuMeterView(label: "FLUX", level: vm.audioService.systemAudioLevel, color: .green)
                    VuMeterView(label: "MIC", level: vm.audioService.micAudioLevel, color: .cyan)
                }
            }
            .padding(8)
            .background(Color(white: 0.08))
            .cornerRadius(8)
            
            // Tiroir transcription dépliable
            if vm.isDetailsExpanded {
                VStack(alignment: .leading, spacing: 6) {
                    Text("Transcription Whisper")
                        .font(.system(size: 11, weight: .semibold))
                        .foregroundColor(.secondary)
                    
                    Text("✨ La transcription complète sera générée automatiquement par Whisper dès l'arrêt de l'enregistrement.")
                        .font(.system(size: 11))
                        .italic()
                        .foregroundColor(.gray)
                        .padding(6)
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .background(Color(white: 0.1))
                        .cornerRadius(6)
                }
                .padding(8)
                .background(Color(white: 0.08))
                .cornerRadius(8)
            }
        }
        .padding(14)
        .frame(width: 480)
        .background(Color(red: 0.05, green: 0.05, blue: 0.08))
        .cornerRadius(16)
        .overlay(
            RoundedRectangle(cornerRadius: 16)
                .stroke(Color(white: 0.16), lineWidth: 1)
        )
        .sheet(isPresented: $vm.showHistory) {
            MacHistoryView(vm: HistoryViewModel())
        }
        .sheet(isPresented: $vm.showSettings) {
            MacSettingsView(vm: SettingsViewModel())
        }
    }
}

#endif
