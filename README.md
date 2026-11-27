# Meeting Capture — Desktop & Mobile Intelligence

**Application multi-plateforme autonome (Windows, macOS et iOS) de capture, archivage et intelligence de réunion**

---

## Présentation

**Meeting Capture** est une solution complète indépendante de Microsoft Teams, capable de capturer simultanément la vidéo, l'audio isolé de la réunion, le microphone, le chat et de produire la transcription automatique Whisper de toute visioconférence.

L'application est disponible sous deux déclinaisons natives optimisées :
1. **Version Windows (10/11 x64)** : Développée en C# / .NET 10 WPF avec capture GDI / DirectComposition DWM (`PrintWindow`), audio WASAPI Loopback par processus, et lecteur multimédia synchronisé.
2. **Version Apple (macOS & iOS)** : Développée en Swift 5.9 / SwiftUI dans le dossier `apple/` avec **ScreenCaptureKit** (macOS) pour la capture isolée de fenêtre et d'audio applicatif, et **ReplayKit** (iOS).

---

## Fonctionnalités multi-plateformes

| Fonctionnalité | Windows (.NET 10) | macOS (ScreenCaptureKit) | iOS (ReplayKit) |
|---|---|---|---|
| UI Flottante Studio | ✅ Opérationnel (WPF Dark) | ✅ Opérationnel (SwiftUI) | ✅ Interface Mobile |
| Capture Vidéo Isolée | ✅ PrintWindow DWM | ✅ SCShareableContent | ✅ RPScreenRecorder |
| Audio Isolé (Loopback) | ✅ WASAPI Loopback | ✅ SCStream Audio | ✅ AVAudioEngine |
| Anti-Collision & Versioning | ✅ Versions auto `_v1`, `_v2` | ✅ Versions auto `_v1`, `_v2` | ✅ Versions auto `_v1`, `_v2` |
| Saisie du titre en direct | ✅ Sur widget principal | ✅ Sur widget principal | ✅ Sur vue principale |
| Relecture Synchronisée | ✅ Vidéo + Audio + Texte | ✅ AVPlayer + Timecode | ✅ AVPlayer tactile |
| Transcription Whisper | ✅ Whisper Python | ✅ Whisper Python / Speech | ✅ Apple Speech |
| Export ZIP de session | ✅ SaveFileDialog natif | ✅ NSSavePanel natif | ✅ UIActivityViewController |

---

## Installation et lancement

### Pré-requis
- Windows 10 (1803+) ou Windows 11
- Droits administrateur recommandés (capture audio WASAPI)
- Permission **Microphone** accordée dans les Paramètres Windows

### Lancement rapide
```
publish\MeetingCapture-win-x64\MeetingCapture.UI.exe
```

L'exécutable est **100% autonome** : .NET 10 est embarqué, aucune installation préalable n'est nécessaire.

---

## Compilation depuis les sources

### Pré-requis
- .NET SDK 10.0.401+

### Build Debug
```powershell
dotnet build MeetingCapture.sln
```

### Build Release + Tests
```powershell
dotnet test tests/MeetingCapture.Tests/MeetingCapture.Tests.csproj
dotnet build MeetingCapture.sln -c Release
```

### Publication Windows x64 autonome
```powershell
dotnet publish src/MeetingCapture.UI/MeetingCapture.UI.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -o publish/MeetingCapture-win-x64
```

---

## Architecture

```
MeetingCapture.sln
├── src/
│   ├── MeetingCapture.Core          → Interfaces, modèles, orchestrateur (IMeetingOrchestrator)
│   ├── MeetingCapture.UI            → WPF + MVVM (MainWindow flottante, HistoryWindow, SettingsWindow)
│   ├── MeetingCapture.Video         → Capture fenêtre/écran (GDI/PrintWindow + VideoStreamWriter AVI/MJPEG)
│   ├── MeetingCapture.Audio         → WASAPI Loopback (système) + WASAPI Capture (micro) + AudioMixer
│   ├── MeetingCapture.Transcription → Moteur de transcription modulaire (Whisper-ready)
│   ├── MeetingCapture.Chat          → Capture et import de messages (JSON / TXT / CSV)
│   ├── MeetingCapture.Synchronization → Master Clock monotonique haute résolution
│   ├── MeetingCapture.Storage       → Archivage structuré, historique, export ZIP
│   ├── MeetingCapture.AI            → Analyse IA : résumé exécutif, décisions, plan d'actions
│   └── MeetingCapture.Integrations  → Détection fenêtres Teams, suivi processus
└── tests/
    └── MeetingCapture.Tests         → 31 tests unitaires et d'intégration (xUnit)
```

---

## Structure des archives

```
Documents/MeetingCapture/
└── 2026/11/23/
    └── Meeting_18-30/
        ├── video/      → recording.mp4
        ├── audio/      → system_audio.wav | microphone.wav | mixed_audio.m4a
        ├── transcript/ → transcript.txt | .json | .srt | .vtt
        ├── chat/       → chat.txt | chat.json
        ├── metadata/   → meeting.json | capture_sources.json
        └── logs/       → recording.log
```

---

## Interface utilisateur

- **Widget flottant compact** toujours visible au-dessus de Teams
- Bouton **REC** pulsant en rouge pendant l'enregistrement
- VU-mètre audio en temps réel (système + microphone)
- Indicateurs : FPS vidéo, segments transcrits, messages chat
- **Transcription live** dans le panneau expansible
- **Fenêtre Historique** : master-detail, synthèse IA, transcription complète, chat, métadonnées
- **Fenêtre Paramètres** : sélection fenêtre, écran, périphériques audio, FPS

---

## Confidentialité

- Enregistrement uniquement à la demande de l'utilisateur (bouton REC)
- Indicateur REC visible en permanence
- Stockage local par défaut (`Documents/MeetingCapture`)
- Aucun envoi de données vers le cloud sans configuration explicite
- Services externes (LLM cloud) désactivés par défaut
- Arrêt immédiat et finalisation propre sur STOP

---

## Limitations Connues

| Situation | Comportement |
|---|---|
| Teams avec protection DRM (`SetWindowDisplayAffinity`) | Frames noires détectées, loguées, basculement recommandé sur capture bureau |
| Changement de périphérique audio en cours d'enregistrement | Reconnexion automatique via IMMNotificationClient (robuste) |
| Transcription Whisper locale | Architecture prête, modèle ONNX à brancher en phase 2 |
| Chat Teams Live (API Graph) | Architecture import-ready, nécessite auth OAuth Teams en phase 2 |
| Encodage MP4 natif sans FFmpeg | Format AVI/MJPEG en sortie ; reconversion MP4 via FFmpeg optionnel |

---

## Développement futur (Phase 2)

- Intégration **Whisper.NET / ONNX** pour transcription locale temps réel
- Connecteur **Ollama** pour analyse IA privée (résumés, extraction)
- **Microsoft Graph API** pour chat Teams officiel
- **Windows Graphics Capture (WGC)** pour remplacement complet du moteur vidéo
- Interface **base vectorielle locale** pour recherche sémantique multi-réunions
- Packaging **MSIX / WinGet** pour distribution
