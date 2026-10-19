# SOURCE — робо-животные

13 робо-животных в этой папке — **производные** ассеты: риги, веса, клипы и исходная
оболочка взяты из CC0-паков Quaternius, робо-облик (череп, панели, броня суставов, хвост)
собран нашими скриптами `Tools/Blender/animal_*.py`. Пак в `Assets/ThirdParty/` не
импортируется: в Unity попадают только собранные FBX, поэтому запись об источнике лежит
здесь. Контракт и правила — `docs/ASSETS.md`, причина решения — ADR 0009.

| Вид | Пак | Файл в паке |
|---|---|---|
| Cat, Dog, Eagle, Piranha, Wolf | `AssetSource/quaternius_animals_vol2.zip` — «Animal Pack Vol.2 by @Quaternius» | `Animal Pack Vol.2 by @Quaternius/FBX/<Name>.fbx` |
| Cow, Horse, Llama, Pig, Pug, Sheep, Zebra | `AssetSource/quaternius_animals_farm.zip` — «Farm Animals Pack by Quaternius» | `Farm Animals by @Quaternius/FBX/<Name>.fbx` |
| Spider | — (собран `animal_designs.make_spider()`, свой риг) | — |

- Лицензия: **CC0 1.0 Universal** (Public Domain Dedication). Подтверждение —
  `Farm Animals by @Quaternius/License.txt` внутри архива. У `Animal Pack Vol.2` файла
  лицензии в архиве нет; зафиксировано в `docs/ASSETS.md`: все паки Quaternius — CC0.
- Что именно взято из источника: скелет, веса вершин, клипы `Idle` и движения
  (`Walking`/`Walk`/`Jump`/`Flying`/`Swimming`) и органическая оболочка. Голова вырезана
  по весам, поверх добавлена робо-броня.
- Архивы лежат в `AssetSource/` — вне Unity и вне git. Восстановление: по именам архивов
  и паков выше; дата скачивания и страница загрузки в проекте не зафиксированы.
- Оси и «вперёд» у паков разные (`+X`, `−Y`, `+Y`); к Unity (+Z) модель приводится
  `animal_lib.py`, итоговое рыскание префаба — в ADR 0009.
