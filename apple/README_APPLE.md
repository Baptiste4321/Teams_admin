# 🍎 MeetingCapture pour Apple (macOS & iOS)

Ce dossier contient l'implémentation native pour **macOS (13.0+ Ventura, Sonoma, Sequoia)** et **iOS (16.0+)** écrite en **Swift 5.9+ / SwiftUI**.

---

## 🚀 Fonctionnalités Clés sur Apple

1. **macOS — ScreenCaptureKit & Process Audio Loopback** :
   - Capture vidéo ciblée de la fenêtre Microsoft Teams (ou écran complet) sans enregistrer les fenêtres superposées.
   - Capture de l'audio de la réunion **directement via le flux d'application SCStream** (l'équivalent Apple du loopback WASAPI sous Windows), fonctionnant même si les haut-parleurs sont muets ou éteints.
   - VU-mètres temps réel (Flux réunion & Micro).
   - Widget flottant Studio avec option Always-On-Top (Épingle 📌) et saisie directe du titre de réunion.
   - Lecteur multimédia synchronisé avec timeline fluide, relecture audio/vidéo AVPlayer, transcription interactive par timecode et export ZIP natif (`NSSavePanel`).

2. **iOS — ReplayKit & Interface Tactile** :
   - Enregistrement de l'écran et du son avec l'API système `ReplayKit`.
   - Interface tactile mobile (`TabView` avec onglets Enregistrer, Historique, Réglages).
   - Relecture des réunions, inspection de la transcription et partage direct de l'archive ZIP.

3. **Système de Fichiers & Versioning automatique** :
   - Stockage identique à Windows : `~/Documents/MeetingCapture/YYYY/MM/DD/Titre`
   - **Protection anti-collision automatique** : Si deux réunions ont le même nom le même jour, l'application génère automatiquement des versions uniques `_v1`, `_v2`, `_v3`... sans jamais écraser la vidéo ni les métadonnées.

---

## 🛠️ Instructions de Compilation & Exécution sur Mac

### Option 1 : Avec Xcode (Recommandé)

1. Transférez le dossier complet ou clonez le dépôt Git sur votre Mac.
2. Ouvrez le projet dans Xcode :
   ```bash
   cd apple
   open MeetingCapture.xcodeproj
   ```
3. Dans la barre supérieure de Xcode, choisissez votre cible :
   - Pour Mac : sélectionnez **My Mac**
   - Pour iPhone / iPad : sélectionnez un **Simulateur iOS** ou votre **iPhone connecté**
4. Appuyez sur **⌘ + R** (Product > Run) pour compiler et démarrer l'application.

---

### Option 2 : En Ligne de Commande (Swift Package Manager)

Sur votre Mac, vous pouvez compiler et exécuter l'application sans même ouvrir Xcode :
```bash
cd apple
swift build -c release
.build/release/MeetingCapture
```

---

## 🔒 Autorisations requises (Permissions Système)

Lors du premier lancement sur macOS ou iOS, le système d'exploitation vous demandera :
1. **Enregistrement de l'écran (Screen Recording)** : Pour permettre à `ScreenCaptureKit` de capturer la fenêtre Teams.
   - Rendez-vous dans *Réglages Système > Confidentialité et sécurité > Enregistrement de l'écran* et activez **MeetingCapture**.
2. **Microphone** : Pour capturer éventuellement votre voix locale.
3. **Reconnaissance vocale** : Pour la transcription audio native.

---

## 🎙️ Transcription Whisper Locale sur Mac

Pour utiliser le modèle d'IA Whisper en local sur votre Mac :
```bash
# Vérifier Python 3 et installer openai-whisper
pip3 install openai-whisper numpy
```
MeetingCapture invoquera automatiquement le script `scripts/transcribe_wav.py` en arrière-plan à la fin de chaque enregistrement.
