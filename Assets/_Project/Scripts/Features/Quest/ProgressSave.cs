using System;
using UnityEngine;

namespace FrostSpace.Features.Quest
{
    /// <summary>
    /// Сохранение прогресса демо: один ключ <see cref="PlayerPrefs"/> и JSON
    /// (ADR 0031). Хранится только то, что нельзя вывести из сцены — сцена,
    /// корпус, квест, его счётчик и опыт игрока. Позиция игрока и предметы не сохраняются:
    /// каждая сцена стартует со своей точки появления.
    ///
    /// Запись идёт сразу через <see cref="PlayerPrefs.Save"/>: в WebGL вкладку
    /// могут закрыть в любой момент. Битое, чужое по версии или неизвестное
    /// сохранение стирается — лучше начать заново, чем восстановить половину.
    /// </summary>
    public static class ProgressSave
    {
        /// <summary>Единственный ключ сохранения.</summary>
        public const string Key = "FrostSpace.Save";

        /// <summary>Версия формата; несовпадение стирает сохранение.</summary>
        public const int CurrentVersion = 1;

        public const string TrainScene = "Train";
        public const string FrontierScene = "Frontier";

        /// <summary>Сохранение есть и читается.</summary>
        public static bool HasSave => TryLoad(out _);

        /// <summary>
        /// Сцена, в которую вернёт «Продолжить»; пустая строка, если сохранения нет.
        /// </summary>
        public static string SavedScene => TryLoad(out var data) ? data.scene : string.Empty;

        /// <summary>
        /// Читает сохранение. Битый JSON, чужая версия, неизвестная сцена или
        /// состояние квеста стирают ключ и дают <c>false</c>.
        /// </summary>
        public static bool TryLoad(out ProgressData data)
        {
            data = null;
            if (!PlayerPrefs.HasKey(Key))
                return false;

            var json = PlayerPrefs.GetString(Key, string.Empty);
            if (string.IsNullOrWhiteSpace(json))
            {
                Clear();
                return false;
            }

            ProgressData parsed;
            try
            {
                parsed = JsonUtility.FromJson<ProgressData>(json);
            }
            catch (Exception)
            {
                Clear();
                return false;
            }

            if (!IsValid(parsed))
            {
                Clear();
                return false;
            }

            data = parsed;
            return true;
        }

        /// <summary>
        /// Записывает прогресс квеста. Сцена вне сюжетного потока заменяется на
        /// <see cref="TrainScene"/>, чтобы «Продолжить» не вело в пустоту.
        /// </summary>
        public static void Write(
            string scene, string characterId, string questId, QuestSnapshot snapshot,
            int level, int experience)
        {
            if (string.IsNullOrWhiteSpace(questId))
                return;

            var data = new ProgressData
            {
                version = CurrentVersion,
                scene = IsKnownScene(scene) ? scene : TrainScene,
                characterId = characterId ?? string.Empty,
                questId = questId,
                state = snapshot.State.ToString(),
                objective = snapshot.ObjectiveIndex,
                progress = snapshot.Progress,
                level = level,
                experience = experience,
            };

            PlayerPrefs.SetString(Key, JsonUtility.ToJson(data));
            PlayerPrefs.Save();
        }

        /// <summary>
        /// Переносит сохранение в другую сцену потока, не трогая прогресс квеста.
        /// Так депо сообщает, что игрок до него дошёл.
        /// </summary>
        public static bool MarkScene(string scene)
        {
            if (!IsKnownScene(scene) || !TryLoad(out var data))
                return false;

            if (string.Equals(data.scene, scene, StringComparison.Ordinal))
                return true;

            data.scene = scene;
            PlayerPrefs.SetString(Key, JsonUtility.ToJson(data));
            PlayerPrefs.Save();
            return true;
        }

        /// <summary>Стирает сохранение: «Начать» в меню и невалидные данные.</summary>
        public static void Clear()
        {
            if (!PlayerPrefs.HasKey(Key))
                return;

            PlayerPrefs.DeleteKey(Key);
            PlayerPrefs.Save();
        }

        /// <summary>Сцена, в которую разрешено возвращаться.</summary>
        public static bool IsKnownScene(string scene) =>
            string.Equals(scene, TrainScene, StringComparison.Ordinal) ||
            string.Equals(scene, FrontierScene, StringComparison.Ordinal);

        /// <summary>
        /// Состояние квеста из сохранения. Числовые строки и мусор не проходят:
        /// <see cref="Enum.TryParse{TEnum}(string, out TEnum)"/> принял бы «7».
        /// </summary>
        public static bool TryGetState(ProgressData data, out QuestState state)
        {
            state = QuestState.Inactive;
            if (data == null || string.IsNullOrWhiteSpace(data.state))
                return false;

            return Enum.TryParse(data.state, false, out state) &&
                   Enum.IsDefined(typeof(QuestState), state);
        }

        private static bool IsValid(ProgressData data)
        {
            if (data == null)
                return false;
            if (data.version != CurrentVersion)
                return false;
            if (!IsKnownScene(data.scene))
                return false;
            if (string.IsNullOrWhiteSpace(data.questId))
                return false;
            if (data.level < 0 || data.experience < 0)
                return false;

            return TryGetState(data, out _);
        }
    }

    /// <summary>
    /// Формат ключа `FrostSpace.Save`. Поля публичные и в нижнем регистре,
    /// потому что это wire-формат JSON, а не данные для инспектора.
    /// </summary>
    [Serializable]
    public sealed class ProgressData
    {
        public int version;
        public string scene;
        public string characterId;
        public string questId;
        public string state;
        public int objective;
        public int progress;
        public int level;
        public int experience;
    }
}
