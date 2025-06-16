# Unity-TTS-system

This repository showcases my current coding skills through one of the largest systems I’ve implemented to date: a Text-to-Speech (TTS) system originally developed as part of a broader VR simulation framework. 

*Note: This project is intended for demonstration purposes only. It is currently non-functional due to lack of maintenance and dependency issues, as the Oculus Voice SDK plugin (which the system relies on) has continued to receive updates since this code was written.*

🚩I suggest you start with the **TTSManager.cs** file, it is the primary class of the system.

### Overview
The TTS (Text-to-Speech) system is responsible for converting text into audio clips and managing their lifecycle, primarily in order to increase accessibility in VR experiences. It is optimized to dynamically download and cache audio clips based on which ones are likely to be used next. The system is built in a modular way, offering ready-to-use default components for quick integration, while also allowing for customization and extension when needed.

The classes used throughout the system are:
* **TTSManager**: the main class of the system; downloads audio clips and stores them
* **ITTSDownloadHelper**: interface that decides the order in which audio clips will be downloaded
* **TTSDownloadHelper**: the default implementation of the ITTSDownloadHelper interface
* **TTSSpeakerController**: plays audio clips; is placed as a child of a UI game object or other sources of text
* **ITTSInputSource**: interface that is implemented by UI objects or other sources of text; used by the TTSpeakerController in order to get the audio that should be played
* **TTSAudioList**: support class that provides audio clips; is a property of ITTSInputSource, and used by the TTSSpeakerController in order to play audio
* **TTSCacheHandler**: support class that can be used by ITTSInputSource objects in order to provide audio for the TTSSpeakerController; contains a TTSAudioList
* **TTSAudioClipCluster**: container class that is used for managing audio clusters (texts that were too large to be processed as a single audio clip, so they were split into multiple smaller ones)
