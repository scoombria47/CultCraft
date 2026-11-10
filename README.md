# CultCraft

[English](#english) · [Русский](#русский)

## English

CultCraft brings Steve, Alex, and Minecraft gear into Cult of the Lamb. Version **0.8.7** supports two-player co-op on a shared screen, including through Steam Remote Play Together.

### Play and install

- **Play through Melty.** Install Cult of the Lamb and Minecraft: Java Edition. Then open [CultCraft on Melty](https://melty.gg/m/cultcraft) and select **Play**. Melty installs BepInEx and the mod.
- **Install the ZIP manually.** Download `CultCraft-0.8.7.zip` from [GitHub](downloads/CultCraft-0.8.7.zip) and follow the [Windows installation guide](INSTALL.md#вручную-из-готового-архива).
- **Build from source.** For a personal Windows build, follow the [installation and build guide in Russian](INSTALL.md).
- **Add a second player.** Load a save, open the pause menu, select **Add Co-Op Player**, and assign the second controller. For two PCs, invite your friend through Steam Remote Play Together before adding them in the game. Both players share the host's screen.

### What’s new in 0.8.7

**0.8.7 fixes startup compatibility.** Minecraft: Java Edition must be installed, but you no longer need to install version 1.21 separately. If your installed client is missing required resources, CultCraft downloads and verifies the official 1.21 resource archive in its own cache. The first download requires an internet connection.

In 0.8.6, gathering uses the correct axe or pickaxe. Reward choices show compact 3D blocks and properly sized item icons. The player carries wrapped followers overhead and moves through a seated pose when getting into and out of bed. Scripted gestures now follow the native animations, and **B** uses a Minecraft-style crouch.

### Features in 0.8.7

- **Steve, Alex, and custom skins.** The Lamb appears as Steve and the Goat as Alex by default. Each character has a separate skin and arm model in **F6**. Enter a Minecraft Java username to load a skin from Mojang, then choose automatic, classic, or slim arms. Loading a skin requires internet access.
- **Camera and settings.** **F5** switches between top-down and first-person views. **F6** opens Minecraft-style settings for mouse sensitivity, field of view, dropped-item size, and hints. The menu supports mouse, keyboard, and controller input.
- **Weapons and combat.** Swords and axes use Minecraft models and material tiers. Hammers look like maces, and guns look like loaded crossbows that reload after three shots. The bow has a separate cooldown, **R** gives quick access to a ranged shot, and the attack indicator follows weapon readiness.
- **Hotbar and food.** **1** selects melee, **2** selects the bow, and **3** selects food. In peaceful areas, hold **1** and scroll to choose the sword or an empty hand. Hold **3** and scroll to pick food, then hold the left mouse button to eat it. Food restores or grants different kinds of hearts, and taking a hit interrupts eating.
- **Resources and settlement.** Dropped items spin in 3D. Resources, crops, seeds, fertilizer, gifts, and scrolls use Minecraft visuals. The settlement features Minecraft-style trees, growing berry bushes, beds, a campfire, and a crafting table. Devotion looks and sounds like Minecraft experience while keeping its role in the game.
- **Animations and sound.** Steve and Alex have animations for gathering, fishing, cleaning, construction, rituals, and temple ceremonies. Their chosen skins appear in Knucklebones and in encounters with The One Who Waits. Minecraft-style fishing gear, enchantment-book effects, hurt sounds, and death animations complete the presentation.

### Requirements and build notes

Use legitimate copies of both games. The game test used Cult of the Lamb for Windows x64 (Steam build `25223484`), Minecraft: Java Edition `1.21` and BepInEx `5.4.23.5`. Minecraft can stay closed while you play.

CultCraft reads assets from the installed Minecraft client. It can fetch extra assets from official Mojang client archives (`1.21.2`, `1.21.9`, and `26.3`) into its own cache. Game assets and libraries are excluded from this repository.

The configuration tables live in [`sheets/`](sheets/). The build validates those tables and generates `Spec.Generated.cs` before compiling the mod against the installed game and BepInEx libraries. Melty's automated test checks the live scene, character, weapons, camera, and movement without using personal saves.

### Credits and permissions

Concept and direction by the project creator. Original implementation developed with help from OpenAI Codex. Cult of the Lamb belongs to Massive Monster and Minecraft to Mojang/Microsoft. BepInEx and Harmony belong to their respective authors.

The original code is proprietary. The creator permits remixes through Melty. See [LICENSE.txt](LICENSE.txt).

## Русский

CultCraft переносит Стива, Алекс и снаряжение Minecraft в Cult of the Lamb. Версия **0.8.7** поддерживает кооператив на двоих на общем экране, в том числе через Steam Remote Play Together.

### Установка и запуск

- **Через Melty.** Установите Cult of the Lamb и Minecraft: Java Edition. Затем откройте [CultCraft в Melty](https://melty.gg/m/cultcraft) и нажмите **Play**. Melty установит BepInEx и мод.
- **Из готового ZIP.** Скачайте `CultCraft-0.8.7.zip` из [GitHub](downloads/CultCraft-0.8.7.zip) и следуйте [инструкции для Windows](INSTALL.md#вручную-из-готового-архива).
- **Из исходников.** Для личной сборки на Windows следуйте [инструкции по установке и сборке](INSTALL.md).
- **Вдвоём.** Загрузите сохранение, откройте меню паузы, добавьте второго игрока и назначьте ему второй контроллер. Для игры с двух компьютеров сначала пригласите друга через Steam Remote Play Together, затем добавьте его в игре. Оба игрока делят экран ведущего.

### Что нового в 0.8.7

**В 0.8.7 исправлен запуск мода.** Minecraft: Java Edition должна быть установлена, но отдельно устанавливать версию 1.21 больше не нужно. Если в установленном клиенте не хватает ресурсов, CultCraft скачает официальный архив 1.21 в свой кэш и проверит его. Для первой загрузки нужен интернет.

В 0.8.6 при сборе ресурсов появляются топор и кирка. В выборе наград блоки отображаются объёмно, а иконки предметов вписываются в штатный размер. Персонаж несёт свёрток на поднятых руках, садится на край кровати при укладывании и подъёме. Жесты повторяют игровые анимации, а **B** включает приседание в стиле Minecraft.

### Возможности 0.8.7

- **Стив, Алекс и свои скины.** По умолчанию Ягнёнок выглядит как Стив, а Козёл — как Алекс. У каждого персонажа свой скин и тип рук в меню **F6**. Введите ник Minecraft Java, чтобы загрузить скин с серверов Mojang, и выберите тип рук «Авто», «Обычные» или «Тонкие». Для загрузки скина нужен интернет.
- **Камера и настройки.** **F5** переключает вид сверху и вид от первого лица. **F6** открывает меню в стиле Minecraft с настройками чувствительности мыши, поля зрения, размера выпавших предметов и подсказок. Меню поддерживает мышь, клавиатуру и контроллер.
- **Оружие и бой.** Мечи и топоры используют модели и уровни материалов Minecraft. Молоты выглядят как булавы, а пистолеты — как заряженные арбалеты с перезарядкой после трёх выстрелов. У лука своя задержка между выстрелами. **R** позволяет быстро выстрелить, а индикатор показывает готовность оружия.
- **Хотбар и еда.** **1** выбирает оружие ближнего боя, **2** — лук, **3** — еду. В мирной локации зажмите **1** и прокрутите колесо, чтобы выбрать меч или пустую руку. Чтобы выбрать еду, зажмите **3** и прокрутите колесо, затем удерживайте левую кнопку мыши. Еда восстанавливает или добавляет разные виды сердец, а попадание прерывает приём пищи.
- **Ресурсы и поселение.** Выпавшие предметы объёмные и вращаются. Ресурсы, урожай, семена, удобрения, подарки и свитки оформлены в стиле Minecraft. В поселении есть деревья, растущие ягодные кусты, кровати, костёр и верстак в стиле Minecraft. Преданность выглядит и звучит как опыт Minecraft и сохраняет свою роль в игре.
- **Анимации и звуки.** У Стива и Алекс есть анимации сбора ресурсов, рыбалки, уборки, строительства, ритуалов и храмовых церемоний. Выбранные скины отображаются в игре в кости и при встрече с Тем-Кто-Ждёт. Рыболовные снасти, эффекты книги зачарования, звуки урона и анимации смерти выполнены в стиле Minecraft.

### Требования и сборка

Используйте легальные копии обеих игр. Игровой тест прошёл на Cult of the Lamb для Windows x64 (сборка Steam `25223484`), Minecraft: Java Edition `1.21` и BepInEx `5.4.23.5`. Во время игры Minecraft можно держать закрытой.

CultCraft читает ресурсы из установленного клиента Minecraft. Дополнительные ресурсы из официальных клиентских архивов Mojang (`1.21.2`, `1.21.9` и `26.3`) мод может загрузить в свой кэш. Игровые ресурсы и библиотеки в репозиторий не входят.

Таблицы настроек находятся в [`sheets/`](sheets/). Перед компиляцией сборка проверяет таблицы и создаёт `Spec.Generated.cs`, затем использует библиотеки установленной игры и BepInEx. Автоматический тест Melty проверяет игровую сцену, персонажа, оружие, камеру и перемещение без использования личных сохранений.

### Авторы и разрешения

Идея и направление принадлежат создателю проекта. Оригинальная реализация подготовлена при помощи OpenAI Codex. Cult of the Lamb принадлежит Massive Monster, Minecraft — Mojang/Microsoft. Права на BepInEx и Harmony принадлежат их авторам.

Оригинальный код распространяется по закрытой лицензии. Создатель разрешает ремиксы через Melty. См. [LICENSE.txt](LICENSE.txt).

## Socials and support / Соцсети и поддержка

- [YouTube](https://www.youtube.com/@scoombria_)
- [Telegram](https://t.me/canwithscumbria)
- [DonationAlerts](https://donationalerts.com/r/scoombria_) (на продление кодекса)
