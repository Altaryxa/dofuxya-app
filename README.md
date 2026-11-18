# Dofuxya Manager

Organise les fenêtres Dofus et Dofus Retro d'une équipe multicompte : ordre du tour, raccourcis clavier et souris,
fenêtres renommées avec le nom et l'icône de classe du perso, mini-barre flottante, suivi du tour sur Retro.

L'application agit seulement sur les fenêtres : elle n'envoie aucune touche ni aucun clic au jeu et ne lit pas sa
mémoire. Elle ne collecte aucune donnée personnelle.

**Télécharger : https://dofuxya.com/tool** (Windows 10 ou 11, sans droits d'administrateur).

## Compiler

Rien à installer : `build.bat` utilise le compilateur C# fourni avec Windows (.NET Framework 4) et produit
`Dofuxya.exe`. Le détail des fonctions et des réglages est dans [LISEZMOI.txt](LISEZMOI.txt).

## Publier une version

1. Pousser un tag `vX.Y.Z` (par exemple `git tag v1.2.1` puis `git push origin v1.2.1`).
2. L'action « Publier une version » ([.github/workflows/release.yml](.github/workflows/release.yml)) compile
   l'application, fabrique l'installateur `Dofuxya-Setup-X.Y.Z.exe` avec Inno Setup et crée la version sur GitHub.
3. Sous 15 minutes environ, https://dofuxya.com/tool propose la nouvelle version, et les applications installées se
   mettent à jour à leur prochain lancement (fichier https://dofuxya.com/tool/version.txt, empreinte SHA-256
   vérifiée avant installation).

Outil de fan, non affilié à Ankama. Dofus est une marque d'Ankama ; les icônes de classe sont la propriété
d'Ankama Games.
