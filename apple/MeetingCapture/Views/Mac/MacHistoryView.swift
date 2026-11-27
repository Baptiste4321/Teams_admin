#if os(macOS)
import SwiftUI
import AVKit

/// Fenêtre d'Historique et Relecture Multimédia Synchronisée macOS
public struct MacHistoryView: View {
    @ObservedObject var vm: HistoryViewModel
    @Environment(\.presentationMode) var presentationMode
    
    public init(vm: HistoryViewModel) {
        self.vm = vm
    }
    
    public var body: some View {
        VStack(spacing: 0) {
            // Top Bar
            HStack {
                Text("Historique & Relecture Studio")
                    .font(.title3.bold())
                
                Spacer()
                
                // Recherche
                HStack {
                    Image(systemName: "magnifyingglass")
                        .foregroundColor(.secondary)
                    TextField("Filtrer par titre...", text: $vm.filterText)
                        .textFieldStyle(.plain)
                }
                .padding(6)
                .background(Color(white: 0.12))
                .cornerRadius(8)
                .frame(width: 200)
                
                Button("Actualiser") {
                    Task { await vm.loadMeetings() }
                }
                
                Button("Fermer") {
                    presentationMode.wrappedValue.dismiss()
                }
            }
            .padding()
            .background(Color(white: 0.07))
            
            Divider()
            
            // Master-Detail Layout
            HSplitView {
                // Colonne de gauche : Réunions enregistrées
                VStack(alignment: .leading, spacing: 6) {
                    Text("RÉUNIONS ENREGISTRÉES")
                        .font(.system(size: 10, weight: .bold))
                        .foregroundColor(.secondary)
                        .padding(.horizontal, 8)
                        .padding(.top, 8)
                    
                    List(vm.filteredMeetings, selection: $vm.selectedMeeting) { meeting in
                        VStack(alignment: .leading, spacing: 4) {
                            Text(meeting.title)
                                .font(.system(size: 13, weight: .semibold))
                                .foregroundColor(.primary)
                            
                            HStack {
                                Text(meeting.metadata.date)
                                    .font(.system(size: 11))
                                    .foregroundColor(.secondary)
                                Spacer()
                                Text(meeting.hasMedia ? "MÉDIA" : "DOCS")
                                    .font(.system(size: 9, weight: .bold))
                                    .padding(.horizontal, 4)
                                    .padding(.vertical, 2)
                                    .background(Color.blue.opacity(0.3))
                                    .cornerRadius(3)
                            }
                        }
                        .padding(.vertical, 4)
                        .tag(meeting)
                        .onTapGesture {
                            vm.selectMeeting(meeting)
                        }
                    }
                    .listStyle(.sidebar)
                }
                .frame(minWidth: 240, maxWidth: 320)
                
                // Colonne de droite : Relecture + Transcription
                if let meeting = vm.selectedMeeting {
                    VStack(spacing: 12) {
                        // Header Réunion
                        HStack {
                            Text(meeting.title)
                                .font(.headline)
                            Spacer()
                            Button("🎙️ Transcrire avec Whisper") {
                                Task { await vm.retranscribeCurrentMeeting() }
                            }
                            .buttonStyle(.borderedProminent)
                            .tint(.indigo)
                        }
                        
                        // Lecteur Vidéo / Audio AVPlayer
                        if let player = vm.player {
                            VideoPlayer(player: player)
                                .frame(height: 280)
                                .cornerRadius(8)
                        } else {
                            RoundedRectangle(cornerRadius: 8)
                                .fill(Color.black)
                                .frame(height: 280)
                                .overlay(
                                    VStack(spacing: 4) {
                                        Text("📁 Fichier média non présent")
                                            .font(.headline)
                                        Text("La relecture synchronisée de la transcription reste active.")
                                            .font(.caption)
                                            .foregroundColor(.secondary)
                                    }
                                )
                        }
                        
                        // Timeline Scrubbing & Contrôles
                        VStack(spacing: 6) {
                            Slider(value: $vm.currentTime, in: 0...max(1.0, vm.totalDuration)) { editing in
                                if !editing {
                                    vm.seek(to: vm.currentTime)
                                }
                            }
                            
                            HStack {
                                Button(vm.isPlaying ? "⏸ Pause" : "▶ Lecture") {
                                    if vm.isPlaying { vm.pause() } else { vm.play() }
                                }
                                Button("⏹ Stop") {
                                    vm.stop()
                                }
                                Spacer()
                                Text("\(formatTime(vm.currentTime)) / \(formatTime(vm.totalDuration))")
                                    .font(.system(size: 11, design: .monospaced))
                                    .foregroundColor(.secondary)
                            }
                        }
                        
                        // Transcription Synchronisée
                        VStack(alignment: .leading, spacing: 4) {
                            Text("Transcription Synchronisée")
                                .font(.subheadline.bold())
                            
                            ScrollView {
                                LazyVStack(alignment: .leading, spacing: 6) {
                                    ForEach(meeting.transcriptSegments) { seg in
                                        HStack(alignment: .top, spacing: 8) {
                                            Text(String(format: "%.1fs", seg.start))
                                                .font(.system(size: 10, weight: .bold, design: .monospaced))
                                                .foregroundColor(.cyan)
                                                .padding(3)
                                                .background(Color(white: 0.15))
                                                .cornerRadius(4)
                                            
                                            VStack(alignment: .leading, spacing: 2) {
                                                Text(seg.speaker)
                                                    .font(.system(size: 11, weight: .bold))
                                                    .foregroundColor(.indigo)
                                                Text(seg.text)
                                                    .font(.system(size: 12))
                                            }
                                        }
                                        .padding(6)
                                        .frame(maxWidth: .infinity, alignment: .leading)
                                        .background(vm.activeSegment?.id == seg.id ? Color.indigo.opacity(0.3) : Color(white: 0.1))
                                        .cornerRadius(6)
                                        .onTapGesture {
                                            vm.seek(to: seg.start)
                                        }
                                    }
                                }
                            }
                        }
                        
                        Divider()
                        
                        // Actions inférieures
                        HStack {
                            if let msg = vm.exportMessage {
                                Text(msg)
                                    .font(.caption)
                                    .foregroundColor(.green)
                            }
                            
                            Spacer()
                            
                            Button("📦 Exporter en ZIP") {
                                Task { await vm.exportZip() }
                            }
                            
                            Button("🗑 Supprimer") {
                                Task { await vm.deleteMeeting() }
                            }
                            .foregroundColor(.red)
                        }
                    }
                    .padding()
                } else {
                    Text("Sélectionnez une réunion dans la liste de gauche.")
                        .foregroundColor(.secondary)
                        .frame(maxWidth: .infinity, maxHeight: .infinity)
                }
            }
        }
        .frame(minWidth: 850, minHeight: 600)
    }
    
    private func formatTime(_ seconds: Double) -> String {
        let s = Int(seconds)
        return String(format: "%02d:%02d", s / 60, s % 60)
    }
}

#endif
