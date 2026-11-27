#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using Project.LarpCore;
using Project.LinuxOS;

namespace Project.EditorTools
{
    /// <summary>
    /// Утилита редактора для быстрой фиксации расположения монитора на компьютерном столе в сцене Dev_Scene
    /// (также выполняется автоматически при старте Play Mode).
    /// </summary>
    public static class LarpSceneSetupEditor
    {
        [MenuItem("L.A.R.P. Corp/Align Monitor To Apartment Desk")]
        public static void AlignMonitorToDesk()
        {
            var monitorPower = Object.FindAnyObjectByType<MonitorPowerController>();
            var flatGo = GameObject.Find("Flat");
            if (monitorPower == null || flatGo == null)
            {
                Debug.LogWarning("[L.A.R.P. Setup] Не найден MonitorPowerController или Flat в активной сцене.");
                return;
            }

            Transform deskScreen = flatGo.transform.Find("Комната2_10x6/Компьютерный_стол/Монитор/Экран");
            if (deskScreen == null)
            {
                Debug.LogWarning("[L.A.R.P. Setup] Не найден объект Flat/Комната2_10x6/Компьютерный_стол/Монитор/Экран.");
                return;
            }

            Undo.RecordObject(monitorPower.transform, "Align MonitorRoot To Desk");
            const float deskScale = 0.82f;
            monitorPower.transform.localScale = Vector3.one * deskScale;

            Transform frame = monitorPower.transform.Find("MonitorFrame");
            Vector3 frameLocal = frame != null ? frame.localPosition : new Vector3(-0.1f, 1.414f, 0f);
            Vector3 desiredFrameWorld = deskScreen.position + new Vector3(0f, 0.03f, -0.02f);

            monitorPower.transform.position = desiredFrameWorld - frameLocal * deskScale;
            monitorPower.transform.rotation = Quaternion.identity;

            Undo.RecordObject(deskScreen.gameObject, "Disable Placeholder Desk Screen");
            deskScreen.gameObject.SetActive(false);

            Transform sofa = flatGo.transform.Find("Комната3_10x10/Диван_ЮЗ");
            if (sofa != null && sofa.GetComponent<SleepInteractable>() == null)
            {
                Undo.AddComponent<SleepInteractable>(sofa.gameObject);
            }

            Debug.Log("[L.A.R.P. Setup] MonitorRoot выровнен по компьютерному столу, диван настроен для сна!");
        }
    }
}
#endif
