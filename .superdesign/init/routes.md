# Native application surfaces

There are no browser routes. The main target is `main-editor` in src/OsuHitsound.App/MainWindow.axaml, with code in its MainWindow partial classes. The arrange view, overview and volume lane are custom-drawn controls. Preferences, Help and UI Inspector are separate owned windows. This design task concerns the main editor only.
