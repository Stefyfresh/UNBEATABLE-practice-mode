# UNBEATABLE Practice Mode

A mod to add a customizable practice mode to the hit rhythm game UNBEATABLE!

## Features

- Allows you to skip to any desired part of any song, base game or custom.
- Will include a countdown timed with the current beat to count you in.
- Automatically sets character and camera position.
- Removes all notes before the specified time.
- Updates the score and accuracy calculations to start from your desired position.
- Configuration through a managed settings file at `AppData/LocalLow/D-CELL GAMES/UNBEATABLE/practice-mode-settings.txt`, with automatic error checking and feedback.
- Automatically disables score saving if a valid entry for a particular song is found.

## Practice Configuration

The mod automatically creates `practice-mode-settings.txt` inside of your UNBEATABLE data directory once the mod is loaded.

To add an entry to the settings, simply add the song name followed by a colon and then the exact millisecond number you would like the song to start at. Here is an example line:

`My Song Name:12345`

The file is reloaded on every song load or restart, so you do not have to close the game to update it.
Also, the config file supports comments, so if you want to quickly disable a particular song, you can just comment it out.

## BepInEx Configuration

As of v1.3.1, new BepInEx configuration entries have been added:

### EnablePractice
A global toggle for the practice feature of practice mode. 
This makes it possible to disable all practice without commenting out every line in the practice options file.

### EnableOffsetFix 
Enables a fix for offset that can cause issues when using practice mode. This fix will force the game's internal timeline to more accurately follow the audio position. The original implementation consistently gets 15-30 ms offset whenever practice mode is enabled and can also randomly change or drift upon restarts or pausing even when NOT using practice mode. This fix solves all those problems and makes offset repeatability across runs and practice mode enabled/disabled much better. 

NOTE: You may experience a change in your perferred offset when you enable this, however I would highly advise against disabling this just to put your preferred offset back to its original value as it is a very useful and globally relevant feature.

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
