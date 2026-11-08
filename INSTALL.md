# Установка CultCraft на Windows

CultCraft работает внутри Cult of the Lamb и читает нужные ресурсы из установленной Minecraft: Java Edition. Для установки используйте легальные копии обеих игр. Мы осуждаем пиратство и рекомендуем покупать игры у правообладателей. Поддержка рассчитана на официальные версии.

Проверенная конфигурация для **0.8.6**: Cult of the Lamb для Windows x64, Steam build `25223484`, Minecraft: Java Edition `1.21`, BepInEx `5.4.23.5`. Minecraft достаточно запустить один раз до главного меню, затем можно закрыть.

## Через Melty

1. Установите обе игры. В Minecraft Launcher выберите **Java Edition**, создайте установку версии `1.21` и запустите её до главного меню.
2. Откройте [страницу CultCraft в Melty](https://melty.gg/m/cultcraft) и нажмите **Play**. Если Melty ещё не установлена, страница предложит приложение для Windows.
3. Melty установит загрузчик и мод, затем запустит Cult of the Lamb. В игре **F5** переключает камеру, **F6** открывает настройки CultCraft и профили скинов.

## Вручную из готового архива

1. Запустите Minecraft: Java Edition `1.21` до главного меню и закройте игру.
2. Установите [BepInEx 5.4.23.5 для Windows x64](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.5). Распакуйте загрузчик в папку с `Cult Of The Lamb.exe`, запустите игру один раз и закройте её.
3. Скачайте `CultCraft-0.8.6.zip` из [GitHub](downloads/CultCraft-0.8.6.zip). Скопируйте содержимое папки `plugins` из архива в `BepInEx\plugins\CultCraft`. Убедитесь, что DLL лежит по пути `BepInEx\plugins\CultCraft\CultCraft.dll`. При обновлении замените старую DLL и проверьте, что в папке загрузчика осталась одна копия мода.
4. Запустите Cult of the Lamb. Проверьте, что **F6** открывает настройки CultCraft. Для нестандартного расположения Minecraft укажите путь к JAR через `CULTCRAFT_MINECRAFT`, как в примере ниже.

## Вручную из исходников

1. Установите [Python 3 для Windows](https://www.python.org/downloads/windows/) с доступной командой `python` и [стабильный .NET SDK для Windows x64](https://dotnet.microsoft.com/en-us/download).
2. Скачайте архив **Windows x64** из [релиза BepInEx 5.4.23.5](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.5). Распакуйте содержимое рядом с `Cult Of The Lamb.exe`. Запустите игру один раз, дождитесь главного меню и закройте её. Папка `BepInEx\core` должна содержать `BepInEx.dll`.
3. Запустите Minecraft: Java Edition `1.21` до главного меню. При стандартной установке клиент находится в `%APPDATA%\.minecraft\versions\1.21\1.21.jar`.
4. Распакуйте исходники CultCraft и откройте PowerShell в папке с `build.ps1`. Для Steam в стандартной папке и обычного Minecraft Launcher выполните команду:

   ```powershell
   powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -PersonalBuild
   ```

   Если игры установлены в другом месте, укажите свои пути. `-GameRoot` указывает на папку с `Cult Of The Lamb.exe`, `-MinecraftJar` на клиент Minecraft:

   ```powershell
   powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -PersonalBuild -GameRoot 'D:\SteamLibrary\steamapps\common\Cult of the Lamb' -MinecraftJar 'D:\Minecraft\versions\1.21\1.21.jar'
   ```

5. После успешной сборки скопируйте `dist\plugins\CultCraft.dll` в папку игры по пути `BepInEx\plugins\CultCraft\CultCraft.dll`. Папку `CultCraft` можно создать вручную. Если в `BepInEx\plugins` уже лежит другая копия `CultCraft.dll`, сначала уберите её.
6. Запустите Cult of the Lamb. Если Minecraft установлен в нестандартную папку, перед запуском укажите путь к установленному клиенту. Например, из PowerShell в папке Cult of the Lamb при уже открытом Steam:

   ```powershell
   $env:CULTCRAFT_MINECRAFT = 'D:\Minecraft\versions\1.21\1.21.jar'
   & '.\Cult Of The Lamb.exe'
   ```

   Дополнительные ресурсы Minecraft `1.21.2`, `1.21.9` и `26.3` мод берёт из локальной установки или загружает с серверов Mojang в свой кэш. Пока ресурс недоступен, соответствующий предмет сохраняет штатный вид. Звуки также могут использовать игровые замены, если в Minecraft отсутствует нужный файл.

### Проверка и удаление

- Откройте `BepInEx\LogOutput.log`. Строка `CultCraft 0.8.6 loaded` подтверждает загрузку. При ошибке поиска Minecraft проверьте, что версия `1.21` установлена и запускалась, а путь ведёт к JAR клиента.
- Для кооператива загрузите сохранение, откройте меню паузы, добавьте второго игрока и назначьте контроллер. При игре с другого ПК сначала пригласите друга через Steam Remote Play Together. У обоих игроков общий экран.
- Чтобы убрать ручную установку, закройте игру и удалите `BepInEx\plugins\CultCraft\CultCraft.dll`. Загрузчик можно оставить для других модов.

Готовая сборка доступна в Melty и в папке [downloads](downloads/) на GitHub. Условия использования исходников указаны в [LICENSE.txt](LICENSE.txt).
