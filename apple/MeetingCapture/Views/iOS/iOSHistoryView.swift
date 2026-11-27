import SwiftUI
import AVKit

/// Vue d'Historique et Relecture tactile pour iOS
public struct iOSHistoryView: View {
    @ObservedObject var vm: HistoryViewModel
    
    public init(vm: HistoryViewModel) {
        self.vm = vm
    }
    
    public var body: some View {
        List {
            ForEach(vm.filteredMeetings) { meeting in
                NavigationLink(destination: iOSMeetingDetailView(meeting: meeting, vm: vm)) {
                    VStack(alignment: .leading, spacing: 4) {
                        Text(meeting.title)
                            .font(.headline)
                        HStack {
                            Text(meeting.metadata.date)
                                .font(.caption)
                                .foregroundColor(.secondary)
                            Spacer()
                            Text(meeting.hasMedia ? "MÉDIA" : "TRANSCRIPT")
                                .font(.system(size: 9, weight: .bold))
                                .padding(.horizontal, 6)
                                .padding(.vertical, 2)
                                .background(Color.indigo.opacity(0.3))
                                .cornerRadius(4)
                        }
                    }
                    .padding(.vertical, 4)
                }
            }
        }
        .navigationTitle("Réunions")
        .refreshable {
            await vm.loadMeetings()
        }
    }
}

/// Vue détaillée de relecture d'une réunion sur iOS
struct iOSMeetingDetailView: View {
    let meeting: MeetingSession
    @ObservedObject var vm: HistoryViewModel
    
    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 14) {
                // Lecteur vidéo
                if let player = vm.player {
                    VideoPlayer(player: player)
                        .frame(height: 220)
                        .cornerRadius(12)
                }
                
                // Contrôles
                HStack {
                    Button(vm.isPlaying ? "Pause" : "Lecture") {
                        if vm.isPlaying { vm.pause() } else { vm.play() }
                    }
                    .buttonStyle(.borderedProminent)
                    
                    Spacer()
                    
                    Button("Exporter ZIP") {
                        Task { await vm.exportZip() }
                    }
                    .buttonStyle(.bordered)
                }
                
                Divider()
                
                // Transcription
                Text("Transcription")
                    .font(.headline)
                
                ForEach(meeting.transcriptSegments) { seg in
                    VStack(alignment: .leading, spacing: 2) {
                        Text("[\(String(format: "%.1fs", seg.start))] \(seg.speaker)")
                            .font(.caption.bold())
                            .foregroundColor(.cyan)
                        Text(seg.text)
                            .font(.subheadline)
                    }
                    .padding(8)
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .background(Color(white: 0.12))
                    .cornerRadius(8)
                    .onTapGesture {
                        vm.seek(to: seg.start)
                    }
                }
            }
            .padding()
        }
        .navigationTitle(meeting.title)
        .navigationBarTitleDisplayMode(.inline)
        .onAppear {
            vm.selectMeeting(meeting)
        }
    }
}
