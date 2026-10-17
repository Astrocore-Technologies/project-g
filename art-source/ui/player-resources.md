# Портрет и оформление панели ресурсов

17 октября 2026 года. Основа — предоставленный пользователем фрагмент утверждённого концепта: круглый портрет поверх трёх отдельных фигурных планок.

## Игровые файлы

- `Content.Client/UI/Components/PlayerResources.tscn` — редактируемая композиция 360×110, три подписи и три реальные полосы ресурсов.
- `Content.Client/UI/Theme/player-resources.tres` — шрифт, треки и заливки.
- `Content.Client/Assets/UI/Hud/resource-plate.svg` — векторная фигурная планка с двойным контуром и окончанием-стрелкой.
- `Content.Client/Assets/UI/Hud/portrait-rim.svg` — векторная оправа медальона.
- `Content.Client/Assets/UI/Hud/resource-track.svg`, `health-fill.svg`, `mana-fill.svg`, `stamina-fill.svg` — трек и заливки с бликами.
- `Content.Client/Assets/UI/Hud/traveler-portrait.png` — временная иллюстрация для портретной области, созданная встроенным `image_gen` по пользовательскому образцу.
- `Content.Client/Assets/UI/Hud/portrait-circle.gdshader` — круглое отсечение портрета с мягким краем.

Векторы и shader написаны для Project G; сторонний код или графические пакеты для них не использовались. Портрет — временная иллюстрация, назначенная в `Scenes/PlayerAvatar.tscn` через `HudPortrait`. HUD получает текстуру локального игрока; рамка и круглое отсечение принадлежат UI-компоненту. Это не изменение модели персонажа, экипировки или сетевого appearance-контракта. Для автономного стенда изображение назначается отдельно через `PreviewPortrait` в `Tests/UI/HudPreview.tscn`.

## Запрос для портрета

Встроенный `image_gen`, reference — приложенный пользователем crop концепта. Результат скопирован в указанный выше игровой PNG.

```text
Use the attached image strictly as a character/style reference. Extract and reconstruct ONLY the illustrated head-and-shoulders portrait of the same young adult male fantasy traveler from the circular HUD medallion: tousled brown hair, warm light skin, brown eyes, practical brown travel jacket, deep navy scarf, three-quarter side profile looking right with a calm serious expression. Hand-painted anime 3D game portrait, refined painterly brushwork, soft cel shading, warm light on the face, cool blue-gray atmospheric background. Produce one square 512x512 portrait illustration with head and scarf centered and fully inside the central 80 percent, suitable for a circular crop. Match this reference closely. Do NOT include the gold frame, no circular crop, no UI, no health bars, no text, no logos, no extra characters, no decorations. Keep the background opaque muted blue-gray so it can be clipped by the game UI.
```
