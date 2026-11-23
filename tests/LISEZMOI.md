# Tests de la lecture d'écran

Chaque capture de `captures/` (PNG, fenêtre du jeu) est lue par `Dofuxya.exe --essai-lecture`, et le
résultat est comparé à `attendu.txt` : niveau, oméga, succès débloqués, quêtes terminées.

- Vérifier : `powershell -File tests\verifier-lecture.ps1` (après `build.bat`).
- Ajouter une capture : la déposer dans `captures/` (nom parlant : `niveau-200-omega-45.png`,
  `chat-succes.png`…), lancer le script avec `-MettreAJour`, relire `attendu.txt`, puis enregistrer
  les deux.

Le dépôt est public : une capture peut montrer des noms de personnages. La tâche GitHub de
publication lance ces tests avant de fabriquer l'installateur.
