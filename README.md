# Dofuxya Manager

Organise les fenêtres Dofus et Dofus Retro d'une équipe multicompte : ordre du tour, raccourcis clavier et souris,
fenêtres renommées avec le nom et l'icône de classe du perso, mini-barre flottante, suivi du tour sur Retro.

L'application agit seulement sur les fenêtres : elle n'envoie aucune touche ni aucun clic au jeu et ne lit pas sa
mémoire. Elle ne collecte aucune donnée personnelle.

**Télécharger : https://dofuxya.com/tool** (Windows 10 ou 11, sans droits d'administrateur). Les mises à jour
s'installent toutes seules au lancement.

## Fonctions

- Détection des fenêtres Dofus et Dofus Retro ouvertes, mise à jour instantanée.
- Ordre d'initiative, ou ordre manuel en faisant glisser le numéro d'une carte.
- Fenêtres renommées avec le nom du perso et l'icône de sa classe.
- Raccourcis clavier ou souris : fenêtre suivante, précédente, perso principal, et un raccourci par perso.
  Ils marchent dans Dofus seulement, ou partout.
- Roue des persos : dans Dofus, Maj maintenue + clic droit ouvre une roue au curseur ; un clic gauche sur un perso
  passe sur sa fenêtre. La touche à maintenir se règle dans l'onglet Raccourcis.
- « Grouper l'équipe » : amène le perso principal devant et copie les commandes `/invite` des autres persos,
  à coller dans le chat du jeu (Ctrl + V, Entrée).
- Barre des tâches rangée dans l'ordre du tour.
- Mini-barre flottante, horizontale ou verticale.
- Dofus Retro : suivi du tour et agrandissement automatique des fenêtres.
- La croix masque la fenêtre : l'outil reste actif dans la zone de notification. Lancement avec Windows en option.
- Liaison facultative à ton compte dofuxya.com (onglet Options) : chaque fenêtre est reliée à ton personnage du
  même nom, affiché en vert : un clic dessus ouvre sa fiche sur le site. En option, une bulle rappelle chaque jour
  l'Almanax (offrande, bonus, personnages qui ne l'ont pas fait).

## Suivi du tour sur Retro

L'outil lit les notifications de Windows. À régler une fois :

1. dans le jeu, activer les notifications en arrière-plan ;
2. dans Windows, laisser les notifications du jeu activées ;
3. dans Paramètres › Confidentialité et sécurité › Notifications, autoriser les applications à accéder aux
   notifications.

## Données

Réglages et initiatives sont enregistrés sur le PC, dans `%AppData%\Dofuxya` (avec `erreurs.txt` en cas de
plantage). Liée au site, l'outil ne lit que la liste de tes personnages et l'Almanax du jour ; sa clé est chiffrée
par Windows pour ta session, et tu peux retirer l'accès depuis ton profil sur le site. Un problème ? Options › À propos copie un diagnostic à joindre à ton signalement.

## Développement

`build.bat` compile `Dofuxya.exe` avec le compilateur C# fourni avec Windows (.NET Framework 4) : rien à installer.
Chaque tag `vX.Y.Z` poussé sur GitHub publie une version
([.github/workflows/release.yml](.github/workflows/release.yml)) : la page de téléchargement et la mise à jour
automatique suivent d'elles-mêmes.

Outil de fan, non affilié à Ankama. Dofus est une marque d'Ankama ; les icônes de classe sont la propriété
d'Ankama Games.
