# UNBEATABLE Practice Mode

A mod to add a customizable practice mode to the hit rhythm game UNBEATABLE!

## Features

- Allows you to skip to any desired part of any song, base game or custom
- Will include a countdown timed with the current beat to count you in
- Automatically sets character and camera position
- Removes all notes before the specified time
- Updates the score and accuracy calculations to start from your desired position
- Configuration through a managed settings file at `AppData/LocalLow/D-CELL GAMES/UNBEATABLE/practice-mode-settings.txt`, with automatic error checking and feedback
- Automatically disables score saving if a valid entry for a particular song is found

## Configuration

The mod automatically creates `practice-mode-settings.txt` inside of your UNBEATABLE data directory once the mod is loaded.

To add an entry to the settings, simply add the song name followed by a colon and then the exact millisecond number you would like the song to start at. Here is an example line:

`My Song Name:12345`

The file is reloaded on every song load or restart, so you do not have to close the game to update it.
Also, the config file supports comments, so if you want to quickly disable a particular song, you can just comment it out.

## Note

If you were having issues with variable bitrate mp3 files, make sure to update to the latest version!

## Mod Installation Instructions

- Download the latest release of the mod from the releases page, and extract the DLL file from inside the zip
- Download BepInEx from [here](https://github.com/BepInEx/BepInEx/releases) and extract the BepInEx folder from the zip into the main UNBEATABLE game code folder (the one that contains UNBEATABLE.exe). You must extract ALL the files from that zip into the main UNBEATABLE folder (do NOT make a new folder!)
- Run the game once and close it
- Put the mod DLL into the BepInEx\plugins folder

The structure should then be:

<pre>
UNBEATABLE
├─── UNBEATABLE.exe
├─── UNBEATABLE_Data
├─── {some other folders and files}
├─── .doorstop_version
├─── changelog.txt
├─── doorstop_config.ini
├─── winhttp.dll
└─── BepInEx
    ├─── cache
    ├─── config
    ├─── core
    ├─── patchers
    └─── plugins
        ├─── SomeMod.dll
        └─── SomeOtherMod.dll
</pre>

Once the mod is in the folder, restart the game and it should load.
