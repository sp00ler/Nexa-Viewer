Put the application icon here as:

    AppIcon.ico

It becomes the executable icon, the taskbar icon and the icon shown in the top-left
corner of the running window. Nothing else needs changing - the project file and the
window both pick it up only when this file exists, and build fine without it.

Format: Windows .ico, ideally containing 16, 32, 48 and 256 pixel square images.
A single 256x256 image works, but small sizes will look soft when Windows scales it down.
