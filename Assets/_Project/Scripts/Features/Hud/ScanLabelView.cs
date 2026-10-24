using System.Globalization;
using FrostSpace.Features.Scan;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FrostSpace.Features.Hud
{
    /// <summary>
    /// Оформление подписи сканера. Отдельный объект, а не поля HUD: подписей в пуле
    /// несколько, и все они обязаны выглядеть одинаково, даже если стиль поменяют
    /// в одном месте.
    /// </summary>
    public sealed class ScanLabelStyle
    {
        public Color Accent = new(0.302f, 0.902f, 1f, 1f);
        public Color Text = new(0.72f, 0.86f, 0.92f, 1f);
        public Color Dim = new(0.30f, 0.42f, 0.50f, 1f);
        public Color PlateFill = new(0.13f, 0.38f, 0.42f, 0.68f);
        public Color OutlineColor = new(0.02f, 0.05f, 0.07f, 1f);

        public float Lift = 68f;
        public float Arm = 150f;
        public float NameSize = 34f;
        public float CategorySize = 20f;
        public float LineWidth = 3f;
        public float Outline = 0.24f;
        public float PlateHeight = 104f;
        public float PlatePadding = 18f;
    }

    /// <summary>
    /// Одна подпись объекта: выносная линия от точки цели вверх-наискосок, плечо вбок
    /// и плашка с категорией, именем и дистанцией.
    ///
    /// Класс, а не компонент: подпись — это набор элементов Canvas, которыми управляет
    /// <see cref="PlayerHud"/>, и собственный жизненный цикл Unity ей не нужен. Пул
    /// из нескольких таких подписей заменяет прежнюю одну: в режиме сонара цели
    /// подписываются одновременно.
    ///
    /// Ширина плашки пересчитывается только при смене текста: <c>GetPreferredValues</c>
    /// дорогой, а имя и категория у цели не меняются кадр от кадра.
    /// </summary>
    public sealed class ScanLabelView
    {
        private const float MinPlateWidth = 180f;
        private const float MaxPlateWidth = 520f;

        private readonly ScanLabelStyle _style;
        private readonly GameObject _root;
        private readonly RectTransform _rootRect;
        private readonly Image _diagonal;
        private readonly Image _arm;
        private readonly RectTransform _plateRect;
        private readonly CanvasGroup _plateGroup;
        private readonly TextMeshProUGUI _category;
        private readonly TextMeshProUGUI _name;
        private readonly TextMeshProUGUI _distance;

        private string _lastName;
        private string _lastCategory;
        private string _lastDistance;
        private float _plateWidth = MinPlateWidth;

        public ScanLabelView(Transform parent, ScanLabelStyle style)
        {
            _style = style;

            _root = new GameObject("Scan Label", typeof(RectTransform));
            _root.transform.SetParent(parent, false);
            _rootRect = (RectTransform)_root.transform;
            _rootRect.anchorMin = new Vector2(0.5f, 0.5f);
            _rootRect.anchorMax = new Vector2(0.5f, 0.5f);
            _rootRect.pivot = Vector2.zero;
            _rootRect.sizeDelta = Vector2.zero;
            _rootRect.anchoredPosition = Vector2.zero;

            _diagonal = NewLine(_root.transform, "Diagonal", out var diagonalRect);
            diagonalRect.anchorMin = Vector2.zero;
            diagonalRect.anchorMax = Vector2.zero;

            _arm = NewLine(_root.transform, "Arm", out var armRect);
            armRect.anchorMin = Vector2.zero;
            armRect.anchorMax = Vector2.zero;

            var textX = _style.Lift * 0.75f + _style.Arm + 14f;
            var sideBox = new Vector2(460f, _style.CategorySize * 1.5f);

            _plateRect = BuildPlate(_root.transform, textX - _style.PlatePadding, _style.Lift + 2f);
            _plateGroup = _plateRect.GetComponent<CanvasGroup>();

            _category = NewText(_root.transform, "Category", "SCAN", _style.CategorySize, _style.Dim,
                TextAlignmentOptions.Left, sideBox, 10f);
            _category.rectTransform.pivot = new Vector2(0f, 0.5f);
            _category.rectTransform.anchoredPosition =
                new Vector2(textX, _style.Lift + _style.NameSize * 0.8f);

            _name = NewText(_root.transform, "Name", string.Empty, _style.NameSize, _style.Text,
                TextAlignmentOptions.Left, new Vector2(460f, _style.NameSize * 1.4f), 4f);
            _name.rectTransform.pivot = new Vector2(0f, 0.5f);
            _name.rectTransform.anchoredPosition = new Vector2(textX, _style.Lift);

            _distance = NewText(_root.transform, "Distance", string.Empty, _style.CategorySize,
                new Color(_style.Accent.r, _style.Accent.g, _style.Accent.b, 0.75f),
                TextAlignmentOptions.Left, sideBox, 10f);
            _distance.rectTransform.pivot = new Vector2(0f, 0.5f);
            _distance.rectTransform.anchoredPosition =
                new Vector2(textX, _style.Lift - _style.NameSize * 0.8f - 6f);

            _root.SetActive(false);
        }

        /// <summary>Скрывает подпись: цель вышла из каналов или пул кончился.</summary>
        public void Hide()
        {
            if (_root.activeSelf)
                _root.SetActive(false);
        }

        /// <summary>
        /// Ставит подпись в точку Canvas, которую HUD уже спроецировал с экрана.
        /// Проекция и отсев слишком близких подписей — дело HUD: пул обязан знать,
        /// какие точки уже заняты, а подпись про соседей ничего не знает.
        /// </summary>
        public void Show(Vector2 local, ScanView view, bool aim)
        {
            if (!_root.activeSelf)
                _root.SetActive(true);

            _rootRect.anchoredPosition = local;

            var strength = Mathf.Clamp01(view.Strength);
            var dx = _style.Lift * 0.75f;
            PlaceSegment(_diagonal.rectTransform, Vector2.zero, new Vector2(dx, _style.Lift));
            PlaceSegment(_arm.rectTransform, new Vector2(dx, _style.Lift),
                new Vector2(dx + _style.Arm, _style.Lift));

            var lineColor = new Color(_style.Accent.r, _style.Accent.g, _style.Accent.b, 0.95f * strength);
            _diagonal.color = lineColor;
            _arm.color = lineColor;

            _category.text = view.Category;
            // Прицельная цель читается акцентом: её подпись — главная на экране.
            _category.color = WithAlpha(
                Color.Lerp(_style.Dim, _style.Accent, aim ? 1f : 0.75f), strength);

            _name.text = view.Name;
            _name.color = WithAlpha(_style.Text, strength);

            // Инвариантная культура: на русской локали разделитель стал бы запятой
            // и подпись выбивалась бы из латинского стиля HUD.
            var distance = view.Distance.ToString("0.0", CultureInfo.InvariantCulture) + " M";
            if (distance != _lastDistance)
            {
                _lastDistance = distance;
                _distance.text = distance;
            }

            _distance.color = WithAlpha(_style.Accent, 0.9f * strength);

            if (view.Name != _lastName || view.Category != _lastCategory)
            {
                _lastName = view.Name;
                _lastCategory = view.Category;

                // Ширина плашки идёт за текстом: длинные имена не режутся, короткие
                // не тянут плашку в пустоту.
                var width = Mathf.Max(
                    _name.GetPreferredValues(view.Name).x,
                    _category.GetPreferredValues(view.Category).x);
                width = Mathf.Max(width, _distance.GetPreferredValues(distance).x);
                _plateWidth = Mathf.Clamp(width + _style.PlatePadding * 2f, MinPlateWidth, MaxPlateWidth);
            }

            _plateRect.sizeDelta = new Vector2(_plateWidth, _style.PlateHeight);
            _plateGroup.alpha = strength;
        }

        /// <summary>Ставит отрезок между двумя точками в локальных координатах подписи.</summary>
        private void PlaceSegment(RectTransform rect, Vector2 from, Vector2 to)
        {
            var delta = to - from;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(delta.magnitude, _style.LineWidth);
            rect.anchoredPosition = from + delta * 0.5f;
            rect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
        }

        /// <summary>
        /// Подложка под подпись: ровная полупрозрачная заливка цифрового слоя, тонкая
        /// линия сверху и уголки по краям. Без неё имя объекта терялось на светлом снегу.
        /// </summary>
        private RectTransform BuildPlate(Transform parent, float x, float y)
        {
            var plate = new GameObject("Holo Plate", typeof(RectTransform));
            plate.transform.SetParent(parent, false);
            var rect = (RectTransform)plate.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.zero;
            rect.pivot = new Vector2(0f, 0.5f);
            rect.sizeDelta = new Vector2(320f, _style.PlateHeight);
            rect.anchoredPosition = new Vector2(x, y);

            var group = plate.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;

            var fill = NewImage(plate.transform, "Fill", _style.PlateFill);
            Stretch(fill.rectTransform);

            var header = NewImage(plate.transform, "Header", WithAlpha(_style.Accent, 0.55f));
            var headerRect = header.rectTransform;
            headerRect.anchorMin = new Vector2(0f, 1f);
            headerRect.anchorMax = new Vector2(1f, 1f);
            headerRect.pivot = new Vector2(0.5f, 1f);
            headerRect.sizeDelta = new Vector2(-20f, 1.5f);
            headerRect.anchoredPosition = new Vector2(0f, -7f);

            AddBracket(plate.transform, new Vector2(0f, 1f), 1f, -1f);
            AddBracket(plate.transform, new Vector2(0f, 0f), 1f, 1f);
            AddBracket(plate.transform, new Vector2(1f, 1f), -1f, -1f);
            AddBracket(plate.transform, new Vector2(1f, 0f), -1f, 1f);

            return rect;
        }

        /// <summary>Пара штрихов в углу плашки: горизонтальный и вертикальный.</summary>
        private void AddBracket(Transform parent, Vector2 anchor, float dirX, float dirY)
        {
            var color = WithAlpha(_style.Accent, 0.85f);
            var pivot = new Vector2(dirX > 0f ? 0f : 1f, dirY > 0f ? 0f : 1f);

            var horizontal = NewImage(parent, "Bracket H", color);
            var hRect = horizontal.rectTransform;
            hRect.anchorMin = anchor;
            hRect.anchorMax = anchor;
            hRect.pivot = pivot;
            hRect.sizeDelta = new Vector2(18f, 2f);
            hRect.anchoredPosition = new Vector2(dirX * 2f, dirY * 2f);

            var vertical = NewImage(parent, "Bracket V", color);
            var vRect = vertical.rectTransform;
            vRect.anchorMin = anchor;
            vRect.anchorMax = anchor;
            vRect.pivot = pivot;
            vRect.sizeDelta = new Vector2(2f, 18f);
            vRect.anchoredPosition = new Vector2(dirX * 2f, dirY * 2f);
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = Mathf.Clamp01(alpha);
            return color;
        }

        private static Image NewImage(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static Image NewLine(Transform parent, string name, out RectTransform rect)
        {
            var image = NewImage(parent, name, new Color(0.302f, 0.902f, 1f, 0f));
            rect = image.rectTransform;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(0f, 2f);
            return image;
        }

        private TextMeshProUGUI NewText(
            Transform parent, string name, string content, float size, Color color,
            TextAlignmentOptions alignment, Vector2 boxSize, float spacing)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.zero;
            rect.sizeDelta = boxSize;
            rect.anchoredPosition = Vector2.zero;

            var text = go.AddComponent<TextMeshProUGUI>();
            if (TMP_Settings.defaultFontAsset != null)
                text.font = TMP_Settings.defaultFontAsset;
            text.text = content;
            text.fontSize = size;
            text.color = color;
            text.alignment = alignment;
            text.characterSpacing = spacing;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.raycastTarget = false;

            // Обводка создаёт у текста отдельный экземпляр материала (TMP): дефолтный
            // шрифт проекта при этом не мутируется и меню не меняется.
            if (_style.Outline > 0f)
            {
                text.outlineWidth = _style.Outline;
                text.outlineColor = _style.OutlineColor;
            }

            return text;
        }
    }
}
