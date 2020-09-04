# Legend-of-Sephraih

Per default the agents controll the characters. For manual controll disable the agent script and enable the player controll script of a character in the editor.
The character swap mechanic from the project two module prototy was not added for simplicity.
Legacy bots could be reimplemented with updated logic.

Controls for the characters:

Warrior:
- a: multislash
- e: chargeattack

Magician:
- q: firebolt
- e: teleport
- mouse0: firebolt to mouse location

Healer:
- a: selfheal
- q: healbolt
- e: healwave


Various agents can be dis- and enabled by toggling the checkbox at the top of their gameobject, by clicking on it in the unity editor.
It is recommended to toggle them via the arena prefab found in the env folder.

The short movie can alternatively be found with english subtitles on: https://www.youtube.com/watch?v=3PAeLEF0PYQ

To Build run trainings, ML-Agents version 0.14.0 and TensorFlow version 2.0.1 are required.
Unity version 2019.3.0b3 was used for both game prototype of the project 2 module and this thesis project.
Pythong version 3.7 has to be installed on the system.