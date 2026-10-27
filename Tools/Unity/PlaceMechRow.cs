using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Ставит четырёх ходунов рядом перед точкой появления игрока.
///
/// Высота берётся рейкастом по рельефу, а не числом: снег неровный, а `Mech`
/// держит капсулу за рельеф через `_groundStick`, поэтому подобранная на глаз
/// высота даёт висящего в воздухе или утопленного ходуна.
/// Запускается из Aura (`execute_script`), в игре не участвует.
/// </summary>
public static class PlaceMechRow
{
    private static readonly string[] Names =
    {
        "ME_FrogWalker_Model",
        "ME_BeeWalker_Model",
        "ME_PandaWalker_Model",
        "ME_FlamingoWalker_Model",
    };

    /// <summary>Ряд по X: −6/−2/+2/+6, общая линия по Z перед спавном.</summary>
    private static readonly float[] RowX = { -6f, -2f, 2f, 6f };

    private const float RowZ = -3.5f;

    public static void Execute()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var player = GameObject.Find("Player");
        var focus = player != null ? player.transform.position : Vector3.zero;
        var report = new StringBuilder();
        report.AppendLine("[MechRow] сцена " + scene.name
                          + ", игрок " + focus);

        var placed = 0;
        for (var i = 0; i < Names.Length; i++)
        {
            var mech = GameObject.Find(Names[i]);
            if (mech == null)
            {
                report.AppendLine("[MechRow] НЕТ " + Names[i]);
                continue;
            }

            var before = mech.transform.position;
            var target = new Vector3(RowX[i], before.y, RowZ);
            if (!TryGround(target, mech, out var ground))
            {
                report.AppendLine("[MechRow] " + Names[i] + ": рельеф не найден");
                continue;
            }

            target.y = ground;
            mech.transform.position = target;

            var look = focus - target;
            look.y = 0f;
            if (look.sqrMagnitude > 0.01f)
            {
                mech.transform.rotation = Quaternion.LookRotation(look.normalized(),
                    Vector3.up);
            }

            placed++;
            report.AppendLine("[MechRow] " + Names[i]
                + " было " + before.ToString("F2")
                + " стало " + target.ToString("F2")
                + " yaw " + mech.transform.eulerAngles.y.ToString("F1"));
        }

        if (placed > 0)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            report.AppendLine("[MechRow] сцена сохранена, поставлено " + placed);
        }

        Debug.Log(report.ToString());
    }

    /// <summary>
    /// Высота рельефа под точкой: первый сверху вниз коллайдер, который не
    /// принадлежит самому ходуну и не является игроком.
    /// </summary>
    private static bool TryGround(Vector3 at, GameObject self, out float ground)
    {
        ground = at.y;
        var origin = new Vector3(at.x, at.y + 60f, at.z);
        var hits = Physics.RaycastAll(origin, Vector3.down, 200f, ~0,
            QueryTriggerInteraction.Ignore);
        if (hits.Length == 0)
        {
            return false;
        }

        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var hit in hits)
        {
            var owner = hit.collider.transform;
            if (owner.IsChildOf(self.transform) || self.transform.IsChildOf(owner))
            {
                continue;
            }

            if (owner.GetComponentInParent<Mech>() != null
                || owner.GetComponentInParent<CharacterController>() != null)
            {
                continue;
            }

            ground = hit.point.y;
            return true;
        }

        return false;
    }
}
