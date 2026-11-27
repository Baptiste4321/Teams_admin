# PROJET — MEETING CAPTURE DESKTOP
## Application Windows autonome de capture, d'archivage et d'intelligence de réunion

Tu agis comme un **Principal Software Engineer, architecte systèmes Windows, ingénieur multimédia senior, spécialiste C#, .NET, capture audio/vidéo, traitement temps réel et applications desktop**.

Ta mission est de concevoir et développer une véritable application Windows compilable, robuste et utilisable, capable de capturer, synchroniser, archiver et exploiter les données d'une réunion Microsoft Teams.

Je ne veux pas d'une simple maquette UI, d'un prototype superficiel, ni d'un projet qui se contente de produire un plan de développement.

Je veux un véritable logiciel, avec du code fonctionnel, des modules opérationnels, des tests, une compilation et une livraison.

---

# 1. VISION DU PROJET

Créer une application desktop Windows indépendante de Microsoft Teams, nommée provisoirement :

**Meeting Capture — Desktop Intelligence**

L'application fonctionne comme un assistant de réunion léger, capable de se superposer à Teams et de démarrer une session de capture depuis une interface flottante.

L'objectif est de centraliser dans un même dossier les données d'une réunion :

- Vidéo de la réunion.
- Audio des interlocuteurs.
- Audio du microphone de l'utilisateur, facultatif.
- Chat de la réunion, lorsque la source est accessible.
- Transcription.
- Participants, lorsque disponibles.
- Horodatages.
- Métadonnées.
- Logs techniques.
- Résumé généré par IA.
- Décisions prises.
- Actions à effectuer.

Le logiciel doit être conçu pour être indépendant des mécanismes d'enregistrement intégrés à Teams.

Il doit avoir son propre moteur de capture, son propre moteur de traitement multimédia, sa propre gestion des fichiers, son propre système de synchronisation et sa propre interface utilisateur.

L'application doit exploiter au maximum les capacités natives de Windows et les sources multimédias accessibles, tout en diagnostiquant précisément les limitations imposées par les applications, les permissions et le système.

---

# 2. OBJECTIF TECHNIQUE PRIORITAIRE : ÉTUDE DES SOURCES DE CAPTURE

Avant de choisir une méthode de capture, réalise une véritable étude technique des différentes architectures possibles sur Windows.

Ne pars pas automatiquement du principe qu'une simple capture d'écran ou une capture WASAPI Loopback sera suffisante.

Analyse et compare notamment :

- Windows Graphics Capture.
- Desktop Duplication API.
- Media Foundation.
- WASAPI.
- Capture audio loopback.
- Capture des périphériques d'entrée et de sortie.
- Les mécanismes Windows de capture de fenêtres.
- Les possibilités de capture de sources multimédias accessibles.
- Les mécanismes de capture indépendants de l'application.
- Les différences entre capture d'une fenêtre, d'un écran et d'une source multimédia.
- Les limites techniques des applications utilisant des protections de capture.
- Les limitations des environnements Windows modernes.
- Les conséquences des changements de périphériques audio.
- La synchronisation de plusieurs sources.

Pour chaque technologie, documente :

| Technologie | Type de capture | Avantages | Limites | Compatibilité |
|---|---|---|---|---|
| Windows Graphics Capture | Vidéo | Capture native moderne | Restrictions de certaines fenêtres | Windows 10/11 |
| Desktop Duplication | Desktop | Performances élevées | Capture écran et contraintes GPU | Windows |
| WASAPI Loopback | Audio | Capture audio de sortie | Dépend du chemin audio | Windows |
| Media Foundation | Multimédia | Pipeline natif | Complexité et codecs | Windows |

Cette analyse doit être réalisée avant la mise en place de l'architecture définitive.

Ne choisis pas une technologie uniquement parce qu'elle est simple à implémenter.

Évalue la fiabilité, la latence, la consommation CPU/GPU, la qualité, les contraintes techniques et la maintenabilité.

---

# 3. APPLICATION DESKTOP

Développer une véritable application Windows native.

## Technologies privilégiées

- C#.
- .NET 10, ou version LTS compatible si nécessaire.
- WPF.
- MVVM.
- Windows APIs.
- Windows Graphics Capture.
- WASAPI.
- Media Foundation ou FFmpeg.
- JSON pour les métadonnées.
- SQLite pour l'index des réunions si pertinent.

L'application doit pouvoir être publiée en Windows x64 autonome.

Prévoir :

- Installation.
- Exécutable.
- Configuration.
- Logs.
- Mise à jour future.
- Gestion des dépendances.
- Versioning.

---

# 4. INTERFACE FLOTTANTE

Créer une petite interface flottante, moderne et discrète, qui se superpose aux applications de réunion.

La fenêtre doit :

- Être toujours visible.
- Être déplaçable.
- Être positionnable.
- Mémoriser sa position.
- Fonctionner au-dessus de Teams.
- Ne pas gêner la réunion.
- Pouvoir être réduite.
- Pouvoir être minimisée dans la zone de notification Windows.
- Utiliser un design sombre moderne.
- Disposer d'une animation REC discrète.
- Être utilisable en un clic.

## Interface inactive

Afficher :

- Nom du logiciel.
- Bouton REC.
- Indicateur vidéo.
- Indicateur audio.
- Indicateur chat.
- Indicateur transcript.
- Bouton paramètres.

## Interface active

Afficher :

- REC.
- Durée.
- STOP.
- Niveau audio.
- État de la capture vidéo.
- État de la capture audio.
- État de la transcription.
- État du chat.

Les indicateurs doivent correspondre à l'état réel des modules.

Aucune indication ne doit être fictive.

---

# 5. MOTEUR VIDÉO

Développer un module indépendant de capture vidéo.

## Fonctionnalités

- Détection des fenêtres ouvertes.
- Identification des fenêtres Teams.
- Sélection manuelle de la fenêtre.
- Sélection d'écran.
- Capture de fenêtre.
- Capture de bureau en option.
- FPS configurables.
- Résolution configurable.
- Gestion du changement de résolution.
- Gestion de plusieurs écrans.
- Gestion des interruptions.
- Gestion des erreurs.
- Encodage vidéo.
- Compression.
- Sauvegarde progressive.

## Paramètres

- 15 FPS.
- 30 FPS.
- 60 FPS.
- Résolution automatique.
- Résolution personnalisée.
- Codec.
- Bitrate.
- Qualité.
- Dossier de stockage.

Format vidéo final privilégié : MP4.

L'application doit détecter lorsque la capture ne retourne plus d'image exploitable.

Créer un diagnostic permettant de détecter :

- Images noires.
- Images figées.
- Absence de frames.
- Changement de résolution.
- Interruption de capture.
- Erreur GPU.
- Erreur d'encodage.

La capture vidéo doit être indépendante du module audio.

---

# 6. MOTEUR AUDIO

Développer un véritable moteur audio capable de gérer plusieurs sources séparément.

## Sources

- Audio système.
- Périphérique de sortie.
- Microphone.
- Périphériques audio sélectionnés.
- Sources audio accessibles via les interfaces Windows.

## Fonctionnalités

- Sélection du périphérique.
- Détection de changement de périphérique.
- Capture audio.
- Bufferisation.
- Encodage.
- Synchronisation.
- Mesure des niveaux.
- Détection de silence.
- Détection d'erreurs.
- Reprise après interruption.

## Pistes

Lorsque cela est possible, conserver :

- `system_audio.wav`
- `microphone.wav`
- `mixed_audio.m4a`

Ne fusionner les sources qu'en cas de nécessité ou sur demande.

Le logiciel doit distinguer une capture réellement active d'une capture qui a démarré mais ne reçoit aucun signal.

Afficher :

- Niveau audio.
- Samples reçus.
- Fréquence.
- Durée.
- Source.
- Statut.

---

# 7. CHAT DE RÉUNION

Développer un module indépendant de gestion du chat.

Il doit permettre d'enregistrer et de structurer les messages lorsque leur récupération est autorisée et techniquement accessible.

Pour chaque message :

- Auteur.
- Texte.
- Date.
- Heure.
- Timestamp relatif.
- ID éventuel.
- Réponses.
- Liens.
- Pièces jointes disponibles.
- Type de message.

Formats :

- JSON.
- TXT.
- Export CSV facultatif.

Si la récupération automatique n'est pas possible, proposer un système d'import de données et des mécanismes de connexion autorisés.

Ne pas créer un faux chat à partir d'informations inexistantes.

---

# 8. TRANSCRIPTION

Créer un système de transcription modulaire.

Priorité :

1. Utilisation d'un transcript officiel accessible.
2. Transcription locale.
3. Moteur externe configurable, uniquement lorsque l'utilisateur l'autorise.

Étudier l'intégration de Whisper local.

Prévoir :

- Français.
- Anglais.
- Détection de langue.
- Timestamp.
- Segmentation.
- Attribution des intervenants lorsque possible.
- Score de confiance lorsque disponible.
- Export TXT.
- Export JSON.
- Export SRT.
- Export VTT.

Exemple :

{
  "start": 124.45,
  "end": 128.90,
  "speaker": "Intervenant 1",
  "text": "Nous allons déployer cette solution la semaine prochaine."
}

Le moteur doit pouvoir être remplacé sans modifier le reste de l'application.

---

# 9. SYNCHRONISATION

Créer un moteur de synchronisation multimédia.

Toutes les sources doivent partager une référence temporelle.

Au démarrage :

- Générer un ID de réunion.
- Initialiser une référence temporelle.
- Initialiser les sources.
- Enregistrer leurs timestamps.

Pour chaque événement, conserver :

- Heure UTC.
- Heure locale.
- Temps relatif à la réunion.
- ID de session.
- Source.

Synchroniser :

- Vidéo.
- Audio système.
- Microphone.
- Transcript.
- Chat.

Gérer :

- Latence.
- Dérive audio.
- Retard de transcription.
- Timestamps des API.
- Décalage des sources.
- Perte de données.
- Reprise de capture.

Créer un outil de diagnostic des désynchronisations.

---

# 10. ENREGISTREMENT

Le workflow doit être extrêmement simple.

## Démarrage

Lorsque l'utilisateur clique sur REC :

1. Créer un nouvel ID de réunion.
2. Créer un dossier.
3. Initialiser les métadonnées.
4. Initialiser les sources disponibles.
5. Démarrer la capture vidéo.
6. Démarrer la capture audio.
7. Démarrer le microphone si activé.
8. Démarrer la transcription.
9. Démarrer la récupération du chat lorsque disponible.
10. Initialiser la synchronisation.
11. Afficher le statut réel de chaque module.

## Arrêt

Lorsque l'utilisateur clique sur STOP :

1. Arrêter les sources.
2. Finaliser les buffers.
3. Finaliser les vidéos.
4. Finaliser les audios.
5. Finaliser les transcripts.
6. Finaliser le chat.
7. Sauvegarder les métadonnées.
8. Vérifier les fichiers.
9. Créer les exports.
10. Afficher un résumé.

---

# 11. ORGANISATION DES ARCHIVES

Chaque réunion doit posséder son propre dossier.

Exemple :

MeetingCapture/
└── 2026/
    └── 11/
        └── 23/
            └── Meeting_18-30/
                ├── video/
                │   └── recording.mp4
                ├── audio/
                │   ├── system_audio.wav
                │   ├── microphone.wav
                │   └── mixed_audio.m4a
                ├── transcript/
                │   ├── transcript.txt
                │   ├── transcript.json
                │   ├── transcript.srt
                │   └── transcript.vtt
                ├── chat/
                │   ├── chat.txt
                │   └── chat.json
                ├── metadata/
                │   ├── meeting.json
                │   └── capture_sources.json
                └── logs/
                    └── recording.log

L'utilisateur doit pouvoir modifier le dossier racine.

---

# 12. MÉTADONNÉES

Créer un fichier meeting.json.

Il doit contenir :

- ID.
- Nom.
- Date.
- Heure de début.
- Heure de fin.
- Durée.
- Applications associées.
- Sources capturées.
- Statuts.
- Résolution.
- FPS.
- Codecs.
- Périphériques.
- Langue.
- Version du logiciel.
- Erreurs.
- Informations de récupération.

---

# 13. HISTORIQUE

Créer une interface dédiée aux réunions enregistrées.

Afficher :

- Titre.
- Date.
- Durée.
- Taille.
- Présence de vidéo.
- Présence d'audio.
- Présence du transcript.
- Présence du chat.

Actions :

- Ouvrir.
- Lire.
- Écouter.
- Voir la transcription.
- Voir le chat.
- Rechercher.
- Exporter.
- Renommer.
- Supprimer.
- Ouvrir le dossier.

---

# 14. INTELLIGENCE ARTIFICIELLE

Préparer une architecture permettant d'ajouter un moteur IA.

Possibilité d'intégrer :

- Ollama.
- LLM local.
- LLM externe configurable.
- Whisper.
- Embeddings.
- Base vectorielle locale.

Fonctions futures :

- Résumé.
- Compte rendu.
- Décisions.
- Actions.
- Tâches.
- Échéances.
- Questions/réponses.
- Recherche sémantique.
- Recherche dans plusieurs réunions.
- Extraction de personnes et projets.
- Génération de notes.

L'IA doit fonctionner sans cloud lorsque les modèles locaux sont disponibles.

---

# 15. CONFIDENTIALITÉ

Le logiciel doit avoir un système de confidentialité transparent.

- Enregistrement démarré par l'utilisateur.
- Indicateur REC visible.
- Arrêt immédiat.
- Aucune capture furtive.
- Aucune collecte en arrière-plan non déclarée.
- Stockage local par défaut.
- Services externes désactivés par défaut.
- Paramètres de rétention.
- Suppression définitive des archives.
- Gestion des permissions.
- Information claire sur les sources capturées.

---

# 16. ARCHITECTURE

Architecture modulaire :

MeetingCapture.UI
MeetingCapture.Core
MeetingCapture.Video
MeetingCapture.Audio
MeetingCapture.Transcription
MeetingCapture.Chat
MeetingCapture.Integrations
MeetingCapture.Synchronization
MeetingCapture.Storage
MeetingCapture.AI
MeetingCapture.Tests

Interfaces :

- IVideoCaptureService
- IAudioCaptureService
- IMicrophoneService
- ITranscriptionService
- IChatCaptureService
- IMeetingStorageService
- ISynchronizationService
- IMeetingAnalysisService

Utiliser l'injection de dépendances.

Éviter de coupler les modules.

---

# 17. PERFORMANCE

Le logiciel doit être conçu pour :

- Minimiser l'utilisation CPU.
- Utiliser l'accélération matérielle lorsque disponible.
- Minimiser la consommation RAM.
- Utiliser des buffers adaptés.
- Éviter les blocages UI.
- Utiliser des tâches asynchrones.
- Éviter les fuites de mémoire.
- Permettre un enregistrement prolongé.
- Gérer un espace disque insuffisant.
- Éviter les corruptions.

---

# 18. TESTS

Créer des tests unitaires et d'intégration.

Tester :

- Capture vidéo.
- Capture audio.
- Buffers.
- Synchronisation.
- Encodage.
- Sauvegarde.
- Arrêt.
- Crash recovery.
- Import.
- Export.
- Recherche.
- Historique.

Créer un mode test simulé.

Créer également un test de charge pour simuler des enregistrements longs.

---

# 19. COMPILATION

L'application doit pouvoir être compilée.

Prévoir :

- Debug.
- Release.
- Windows x64.
- Publication autonome.
- Runtime intégré.
- Packaging.
- Installation.

Utiliser des commandes dotnet reproductibles.

Compiler réellement le projet.

Corriger les erreurs.

Ne pas annoncer qu'une fonctionnalité est opérationnelle si elle n'a pas été testée.

---

# 20. MÉTHODOLOGIE AGENT

Tu ne dois pas te limiter à générer un plan.

Tu dois agir directement dans l'environnement de développement.

Étape 1 : Inspecter l'environnement.

Étape 2 : Vérifier les outils installés.

Étape 3 : Réaliser l'étude technique des sources multimédias Windows.

Étape 4 : Définir l'architecture.

Étape 5 : Créer le projet.

Étape 6 : Développer l'interface.

Étape 7 : Développer les moteurs de capture.

Étape 8 : Développer le stockage.

Étape 9 : Développer la synchronisation.

Étape 10 : Développer la transcription.

Étape 11 : Développer l'historique.

Étape 12 : Tester.

Étape 13 : Compiler.

Étape 14 : Corriger.

Étape 15 : Produire le build final.

Ne t'arrête pas à la conception.

Ne demande pas de validation à chaque étape.

Prends des décisions techniques cohérentes et documente-les.

Si une fonctionnalité n'est pas réalisable avec les technologies retenues, identifie précisément le blocage, propose une alternative réaliste et continue le développement des autres modules.

---

# 21. LIVRABLE FINAL

À la fin, je veux :

- Application desktop fonctionnelle.
- Code source complet.
- Architecture documentée.
- Interface moderne.
- Capture vidéo opérationnelle.
- Capture audio opérationnelle.
- Microphone optionnel.
- Transcription.
- Chat lorsque accessible.
- Synchronisation.
- Archivage.
- Historique.
- Paramètres.
- Logs.
- Tests.
- Exécutable Windows x64.
- Procédure d'installation.
- README.
- Rapport de compilation.

Produis également un tableau final :

| Fonctionnalité | Statut | Testée | Limitations |
|---|---|---|---|
| UI flottante | | | |
| Capture vidéo | | | |
| Capture audio | | | |
| Microphone | | | |
| Transcript | | | |
| Chat | | | |
| Synchronisation | | | |
| Archivage | | | |
| Historique | | | |
| Build Windows | | | |

Ne prétends pas qu'une fonction est opérationnelle si elle n'est pas réellement implémentée et testée.

---

# OBJECTIF FINAL

Construire un logiciel Windows autonome, fiable et évolutif, servant de plateforme de capture et d'intelligence de réunion.

Je veux une application professionnelle, et non un prototype.

Commence immédiatement par inspecter l'environnement, étudier les possibilités techniques réelles de capture Windows et développer le projet directement.

Privilégie un MVP fonctionnel, puis continue à enrichir le projet jusqu'à obtenir le maximum de fonctionnalités réellement opérationnelles.