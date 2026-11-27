# Étude Technique Complète : Architectures de Capture Multimédia sous Windows (Desktop, Fenêtres & Audio)

## 1. Introduction et Problématique

Dans le cadre du projet **Meeting Capture — Desktop Intelligence**, le logiciel doit capturer, synchroniser et archiver de façon fiable, discrète et autonome des flux audio (rendu système/interlocuteurs distants et microphone local) et des flux vidéo (fenêtre d'application de visioconférence telle que Microsoft Teams ou bureau complet), indépendamment des APIs propriétaires des services de communication.

Sous Windows moderne (Windows 10 version 1803+ et Windows 11), les sous-systèmes graphiques et audio ont profondément évolué (WDDM 2.x/3.x, DWM matériel, sandbox UWP/WinRT, CoreAudio/WASAPI, Media Foundation). Réaliser une capture performante, à faible latence et sans impacter la fluidité de la réunion nécessite d'analyser les différentes couches d'APIs disponibles, leurs forces, leurs verrous de sécurité et leurs contraintes matérielles.

---

## 2. Analyse Comparative des Technologies de Capture Vidéo / Écran / Fenêtre

| Technologie | Type de capture | Avantages | Limites & Contraintes | Compatibilité Windows | Latence & Impact CPU/GPU |
|---|---|---|---|---|---|
| **Windows Graphics Capture (WGC / Windows.Graphics.Capture)** | Fenêtre (HWND) & Écran (HMONITOR) | - Capture directe au niveau DWM (Desktop Window Manager)<br>- Accélération GPU DXGI / Direct3D 11 native<br>- Ne capture pas les fenêtres superposées (ex: notre UI flottante ne masque pas Teams)<br>- Détection automatique du redimensionnement de fenêtre<br>- Pas d'artefacts d'occlusion ou de masquage de fenêtre | - Nécessite Windows 10 1803+ (1903+ pour masquage de bordure jaune via `IsBorderRequired`)<br>- Les fenêtres protégées par DRM (`SetWindowDisplayAffinity(WDA_MONITOR)`) sortent en noir<br>- Nécessite un contexte d'interop WinRT / Win32 (IInspectable / CreateForWindow) | Windows 10 (1803+) & Windows 11 | Latence ultra-faible (<16ms à 60fps), consommation CPU minime (<2%), zéro copie CPU-GPU |
| **Desktop Duplication API (DXGI OutputDuplication)** | Écran entier (Desktop / GPU Output) | - Capture native DirectX 11/12 très haute cadence (jusqu'à 144+ FPS)<br>- Accès direct aux surfaces GPU du bureau<br>- Idéal pour la capture multi-écrans plein écran | - Ne permet pas de cibler directement une fenêtre spécifique HWND sans découpage manuel (crop CPU/GPU)<br>- Problèmes d'adaptateurs multiples (PC portables avec GPU intégré Intel/AMD + GPU dédié NVIDIA)<br>- Toute fenêtre passant par-dessus est capturée (y compris l'interface flottante) | Windows 8, 8.1, 10, 11 | Très faible latence (<10ms), charge GPU faible, mais découpage fenêtre complexe |
| **GDI BitBlt / PrintWindow (Legacy Win32)** | Fenêtre ou Écran | - Simple à mettre en œuvre en C# (`User32.dll`)<br>- Fonctionne sur toutes les versions de Windows sans dépendance moderne | - Très consommateur en cycles CPU (copie mémoire vidéo -> RAM)<br>- `PrintWindow` avec `PW_RENDERFULLCONTENT` produit souvent des écrans noirs avec les applications matérielles accélérées (Chromium / Teams Web / WebView2 / Electron)<br>- Taux de rafraîchissement faible (<15-20 FPS) | Windows 2000 à Windows 11 | Élevée (30-80ms), charge CPU importante (15-30% CPU) |
| **Media Foundation SinkWriter / ScreenCapture** | Pipeline complet Capture + Encodage | - Encodage matériel matériel natif H.264 / HEVC / AAC via Intel QSV, NVIDIA NVENC, AMD AMF<br>- Intégration complète sous forme de source Media Foundation | - API C++ COM complexe à piloter en C# pur<br>- Gestion d'erreurs lourde en cas de perte de périphérique ou de changement de résolution en direct | Windows 7 à 11 | Excellente si couplée à un encodeur hardware |
| **DirectShow (Legacy)** | Périphériques vidéo (Webcam, Capture cards) | - Standard historique pour les webcams physiques | - Obsolète pour la capture d'écran native<br>- Mauvaise gestion du DPI dynamique et de l'accélération matérielle moderne | Windows XP à 11 | Modérée à élevée |

### Synthèse Vidéo
**Choix optimal :** **Windows Graphics Capture (WGC)** complété par une stratégie de fallback sur capture bureau via **Direct3D11 / GDI DIBSection optimisée**.
- WGC permet de capturer la fenêtre Teams de manière isolée sans que d'autres fenêtres (comme notre widget flottant de contrôle) ne viennent polluer l'enregistrement, même si l'utilisateur superpose des notes ou change la taille de la fenêtre.
- Pour l'encodage, l'utilisation de flux MP4 (H.264/AAC) via un moteur d'encodage optimisé garantit une lecture universelle.

---

## 3. Analyse Comparative des Technologies de Capture Audio

| Technologie | Type de capture | Avantages | Limites & Contraintes | Compatibilité Windows | Comportement lors des changements de device |
|---|---|---|---|---|---|
| **WASAPI Loopback (Windows Audio Session API)** | Flux de rendu audio sortant (Speakers, Écouteurs, Casques) | - Capture bit-perfect du signal audio envoyé aux haut-parleurs/casque<br>- Aucun pilote virtuel tiers nécessaire<br>- Latence ultra-faible (mode Event-driven ou Timer-driven)<br>- IAudioClient / IAudioCaptureClient standard Windows | - En mode Loopback standard, ne capture que le flux du périphérique de sortie par défaut<br>- En l'absence de son joué sur le système, WASAPI Loopback peut ne pas émettre de paquets (génération de silence nécessaire pour maintenir la synchro temporelle)<br>- Changement de périphérique (ex: débranchement du casque Bluetooth) invalide le client audio | Windows Vista à 11 | Déclenche `AUDCLNT_E_DEVICE_INVALIDATED` : requiert un mécanisme d'écoute des notifications `IMMNotificationClient` pour reconnexion instantanée sans crash |
| **WASAPI Capture (Microphone / Input)** | Flux d'entrée microphone local | - Capture directe du microphone de l'utilisateur<br>- Niveaux de volume indépendants<br>- Pistes séparées pour l'analyse IA (stéréophonie ou diarisation) | - Nécessite la permission Microphone Windows 10/11 (`Capacités de confidentialité`)<br>- Peut introduire un écho si l'utilisateur n'utilise pas de casque | Windows Vista à 11 | Même résilience nécessaire (`IMMNotificationClient`) |
| **Virtual Audio Cable / VB-Cable / DMO** | Pilote de boucle audio virtuelle | - Possibilité d'isoler des flux d'applications spécifiques sur d'anciennes versions | - Nécessite l'installation d'un pilote tiers (droits administrateur, instabilité potentielle)<br>- Incompatible avec une expérience utilisateur autonome et légère | Windows 7 à 11 | Fragile |
| **WASAPI Process-specific Capture (Windows 10 2004+ / Windows 11)** | Capture audio isolée d'un Process ID donné (`AUDIOCLIENT_ACTIVATION_PARAMS`) | - Permet d'isoler uniquement le son émis par le processus Teams sans capturer les bruits de notification Windows ou d'autres applications | - Ne capture pas les processus enfants si l'architecture Teams (Electron/WebView2) route son audio via un sous-processus utility non répertorié<br>- API WinRT/COM non triviale | Windows 10 2004+ / Windows 11 | Nécessite un repli automatique sur le périphérique par défaut si le PID ciblé n'émet rien |

### Synthèse Audio
**Choix optimal :**
1. **Double flux séparé (Dual-Track)** :
   - Flux 1 : `system_audio.wav` (capture des interlocuteurs distants via WASAPI Loopback sur le périphérique de rendu actif).
   - Flux 2 : `microphone.wav` (capture de la voix locale de l'utilisateur via WASAPI Capture).
2. **Gestion du Silence et de la Continuité Temporelle** :
   - Lorsque WASAPI Loopback ne reçoit aucun paquet (silence complet dans la réunion), l'horloge de capture injecte des trames PCM de silence (zéros) afin que la durée audio corresponde exactement au temps écoulé (`Stopwatch.GetTimestamp()`) et ne provoque aucun décalage avec la vidéo.
3. **Résilience dynamique (Hot-plugging)** :
   - Implémentation d'un écouteur d'événements de périphérique audio (`IMMNotificationClient`) pour détecter le branchement/débranchement de casques/AirPods/haut-parleurs et réinstancier automatiquement le flux audio sans interrompre la session de réunion.
4. **Mixage progressif & finalisation** :
   - Conservation des pistes isolées + génération d'un mixage stéréo/AAC final (`mixed_audio.m4a`).

---

## 4. Analyse des Fenêtres Teams, Protections et Limites Techniques

### 4.1. Nature de Microsoft Teams (Teams Classic vs New Teams 2.0/2.1)
- **Teams Classique** : basé sur Electron (Chromium + Node.js).
- **New Teams (Work or School / Personal)** : basé sur WebView2 / Edge Chromium et WinUI 3 / XAML Island (`ms-teams.exe`).
- **Comportement graphique** : rendu accéléré matériellement par Direct3D 11 via ANGLE/DirectX.
  - La méthode obsolète `PrintWindow(hwnd, hdc, PW_CLIENTONLY)` retourne un cadre noir ou gris.
  - La méthode moderne **Windows Graphics Capture** (`GraphicsCaptureItem.CreateFromVisual` ou interop HWND `CreateForWindow`) accède à la surface de composition DWM et capture parfaitement le contenu rendu de Teams, même si la fenêtre est partiellement recouverte par d'autres applications.

### 4.2. Protections de Capture d'Écran (`SetWindowDisplayAffinity`)
Certaines entreprises configurent Teams avec des stratégies DLP (Data Loss Prevention) ou protection contre la copie d'écran (`WDA_MONITOR` ou `WDA_EXCLUDEFROMCAPTURE`).
- **Diagnostic** : L'application doit analyser le flux de frames capturées. Si toutes les valeurs RGB sont à 0 (black frame) pendant plus de 1.5 seconde alors que le HWND est actif, un avertissement doit être levé dans les logs et les métadonnées (`capture_sources.json`), proposant à l'utilisateur de basculer en mode capture d'écran complète (Desktop capture).

### 4.3. Détection et Suivi Automatique des Fenêtres Teams
- L'application énumère les fenêtres actives (`EnumWindows`) et filtre :
  - `ms-teams.exe`
  - `Teams.exe`
  - Titres contenant `"Microsoft Teams"`, `"Réunion"`, `"Meeting"`, `"Appel"`, `"Call"`.
- Elle conserve l'identifiant HWND et gère la minimisation (lorsqu'une fenêtre est minimisée dans la barre des tâches, DWM cesse de rafraîchir son rendu ; l'application doit détecter l'état `IsIconic(hwnd)` et alerter ou suspendre temporairement la frame).

---

## 5. Synchronisation Temporelle Multi-Sources (Horloge Maîtresse)

### Problématique
Dans un enregistrement combinant vidéo (25/30/60 fps), audio distant (48 kHz stéréo), audio microphone (44.1 ou 48 kHz mono), transcriptions IA asynchrones et messages de chat, le risque majeur est la **dérive temporelle (drift)** et le **décalage initial (skew)**.

### Solution Architecturale : Horloge Monotonique Haute Résolution
1. **Master Clock** :
   - Basée sur `System.Diagnostics.Stopwatch.GetTimestamp()` couplée à `DateTimeOffset.UtcNow` au moment exact de l'appui sur `REC`.
2. **Indexation de chaque échantillon** :
   - Chaque frame vidéo porte son timestamp relatif en millisecondes : `t_rel = (timestamp_frame - timestamp_rec_start) / Frequency`.
   - Chaque bloc audio PCM est comptabilisé en nombre d'échantillons absolu depuis `t0` : `samples_expected = (t_rel * sample_rate)`. Tout écart est comblé par interpolation de silence ou ajustement de buffer.
   - Les segments de transcription et messages de chat reçoivent le `t_rel` exact calculé à partir de la même référence d'horloge.

---

## 6. Transcription et Intelligence Artificielle

### Architecture Hybride Locale & Déconnectée
1. **Moteur Local Intégré** :
   - Intégration modulaire avec support de transcription locale (ex: Whisper.net / ONNX Runtime ou intégration de modèle léger C# / CLI whisper).
   - Possibilité de transcription simulée haute fidélité pour les environnements de test et CI/CD sans GPU dédié.
2. **Connecteur IA Extensible** :
   - Interface `IMeetingAnalysisService` compatible avec des endpoints locaux (Ollama, LM Studio sur `http://localhost:11434`) ou distants sécurisés.
   - Extraction automatique : Résumé exécutif, Décisions clés, Plan d'action (To-Do avec assignations et échéances).

---

## 7. Conclusions et Choix d'Implémentation

1. **Plateforme** : C# .NET 10.0 (Windows Desktop SDK / WPF) en architecture MVVM propre.
2. **Vidéo** : Moteur hybride WGC / D3D11 / ScreenCapture avec encodeur MP4 / AVI / H264 multi-résolutions.
3. **Audio** : Moteur WASAPI Loopback (rendu distant) + WASAPI Input (microphone) avec écoute `IMMNotificationClient` pour reconnexion sans interruption.
4. **Chat & Métadonnées** : Module d'import/capture structuré JSON/TXT avec horodatage milliseconde.
5. **Stockage** : Structure d'archive hiérarchique `MeetingCapture/AAAA/MM/JJ/Meeting_HH-mm/` complète et autonettoyante.
6. **UI** : Widget flottant compact WPF sombre (Dark Mode), toujours au premier plan (`Topmost`), déplaçable, avec états réels et vumètre audio dynamique.
