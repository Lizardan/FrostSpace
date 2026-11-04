using FrostSpace.Features.Locomotion;
using UnityEngine;

namespace FrostSpace.Features.Map
{
    /// <summary>
    /// Резидентность клеток: в сцене живёт только то, что может попасть в кадр.
    ///
    /// Это выключенный GameObject, а не выгруженная сцена (ADR 0036 п.2). Разница
    /// принципиальная: меши остаются в сборке, и в WebGL выгрузка сцены память бы
    /// всё равно не вернула, зато появились бы точки загрузки и оркестрация вокруг
    /// них. `SetActive(false)` снимает с рендера ровно то, что нужно, — draw call
    /// и обход в culling, — и стоит ноль.
    ///
    /// Радиус 1 при клетке 400 м закрывает видимость ровно впритык: игрок
    /// стоит не дальше 200 м от центра своей клетки, видит на 400 м, а
    /// загруженные 3×3 клетки доходят до 600 м от центра. Запас держат не
    /// геометрией, а туманом и дальним слоем: на 400 м туман оставляет
    /// единицы процентов цвета, и край загруженного мира не читается.
    ///
    /// Пересчёт идёт по **смене клетки**, а не по дистанции: пока игрок внутри
    /// своей клетки, набор активных не меняется, и весь апдейт — одно сравнение
    /// двух целых. Порог по пройденным метрам дал бы пересборку списка на каждом
    /// шаге и ничего не добавил бы.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(270)]
    public sealed class WorldResidency : MonoBehaviour
    {
        [Tooltip("Радиус residency в клетках: 1 = своя и восемь соседей")]
        [Range(0, 3)]
        [SerializeField] private int _radius = 1;

        [Tooltip("world_layout.json — из него сетка и перевод осей сцены в мир")]
        [SerializeField] private TextAsset _layout;

        [Tooltip("Игрок; пусто — ищется в сцене по ходовой части")]
        [SerializeField] private Transform _player;

        private readonly System.Collections.Generic.List<WorldCell> _cells = new();
        private readonly System.Collections.Generic.List<Vector2Int> _resident = new();
        private WorldLayout _world;
        private Vector2Int _current = new(int.MinValue, int.MinValue);

        /// <summary>О какой клетке уже предупредили: не спамить каждый кадр.</summary>
        private Vector2Int _guardReported = new(int.MinValue, int.MinValue);

        /// <summary>Сколько клеток включено прямо сейчас — для замера и теста.</summary>
        public int ActiveCount
        {
            get
            {
                var n = 0;
                for (var i = 0; i < _cells.Count; i++)
                {
                    if (_cells[i].gameObject.activeSelf)
                        n++;
                }

                return n;
            }
        }

        private void Awake()
        {
            _cells.Clear();
            GetComponentsInChildren(true, _cells);

            // Один индекс на две клетки — это не «последняя победила», а
            // сломанная сцена: вторая клетка никогда не включится, и
            // молчать об этом нельзя.
            var seen = new System.Collections.Generic.HashSet<Vector2Int>();
            for (var i = 0; i < _cells.Count; i++)
            {
                if (!seen.Add(_cells[i].Cell))
                {
                    Debug.LogError(
                        "WorldResidency: две клетки с индексом " + _cells[i].Cell +
                        " — вторая не будет включаться", _cells[i]);
                }
            }

            if (_player == null)
            {
                // Ходовая часть есть только на игроке: по ней его и находим,
                // вместо тега, который может стоять на ком угодно.
                var body = FindAnyObjectByType<RobotLocomotion>();
                _player = body != null ? body.transform : null;
            }

            if (!WorldLayout.TryParse(_layout, out _world))
            {
                _world = null;
                return;
            }

            Apply(CellUnderfoot());
        }

        private void LateUpdate()
        {
            if (_world == null || _player == null)
                return;

            var cell = CellUnderfoot();
            if (cell == _current)
                return;

            Apply(cell);
        }

        private Vector2Int CellUnderfoot()
        {
            // Позиция игрока — в осях сцены (её ноль = двор депо), сетка живёт в
            // мировых: без перевода игрок в депо попал бы в клетку (0,0), а не
            // в (-2,-2), и резидентность включила бы не то.
            var p = _player.position;
            return _world.Grid.CellOf(_world.ToWorld(new Vector2(p.x, p.z)));
        }

        private void Apply(Vector2Int center)
        {
            _current = center;
            _resident.Clear();
            _world.Grid.AddResidentCells(center, _radius, _resident);

            for (var i = 0; i < _cells.Count; i++)
            {
                var cell = _cells[i];
                var resident = _resident.Contains(cell.Cell);

                // Игрок может оказаться ребёнком клетки: на время прибытия он
                // едет в вагоне, а вагон лежит в префабе депо. Гасить такую
                // клетку нельзя — вместе с ней выключится и герой. Штатно этого
                // не случается (клетка игрока резидентна по определению), но
                // цена ошибки — исчезнувший игрок, поэтому проверка дешёвая.
                if (!resident && _player != null && _player.IsChildOf(cell.transform))
                {
                    if (_guardReported != cell.Cell)
                    {
                        _guardReported = cell.Cell;
                        Debug.LogWarning(
                            "WorldResidency: игрок внутри клетки " + cell.Cell +
                            " — клетка оставлена включённой", cell);
                    }

                    continue;
                }

                cell.gameObject.SetActive(resident);
            }
        }
    }
}
