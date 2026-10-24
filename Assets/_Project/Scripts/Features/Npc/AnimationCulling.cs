using UnityEngine;

namespace FrostSpace.Features.Npc
{
    /// <summary>
    /// Настройка скинной анимации у актёров, которые двигаются кодом (патруль, локомоция,
    /// меню). Одна точка правды для двух флагов, которые ломают движение.
    ///
    /// <list type="bullet">
    /// <item><b>Почему <c>AlwaysAnimate</c>.</b> У моделей из Blender
    /// <c>Animator.cullingMode</c> по умолчанию <c>CullUpdateTransforms</c>: когда Unity
    /// считает рендерер «за кадром», кости перестают обновляться. Стоит отвернуть камеру —
    /// и животное замирает в последней позе, а при возврате взгляда прыгает в текущую:
    /// это читается как «модель съехала с родителя и перескочила».</item>
    /// <item><b>Почему <c>updateWhenOffscreen</c>.</b> Скинные меши из FBX приходят с
    /// <c>localBounds</c> в масштабе арматуры: у волка это 7 см на метровую модель. По таким
    /// границам отсечение врёт (рендерер «не виден» при видимой модели), а сканер берёт
    /// центр этих границ в подпись. Флаг заставляет Unity считать границы по факту.</item>
    /// </list>
    /// </summary>
    public static class AnimationCulling
    {
        /// <summary>Включает постоянную анимацию и честные границы у всех скинных мешей актёра.</summary>
        public static void Apply(Animator animator)
        {
            if (animator == null)
                return;

            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            var renderers = animator.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                    renderers[i].updateWhenOffscreen = true;
            }
        }
    }
}
