using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Project.LarpCore;

namespace Project.LinuxOS
{
    /// <summary>
    /// Приложение «Notes» — точный референс из левого нижнего угла скриншотов:
    /// чистый светлый лист с заметками и строка ввода «Enter note...» снизу (добавление по Enter).
    /// </summary>
    public class NotesWindowApp : MonoBehaviour
    {
        private readonly List<string> _notes = new List<string>
        {
            "Смена #1 — проверить тикет Маркуса в Mail",
            "Wi-Fi: подключиться к открытой сети в трее"
        };

        private RectTransform _listContent;
        private TMP_InputField _noteInput;

        public void Initialize(RectTransform root)
        {
            var v = root.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = 0;
            v.padding = new RectOffset(0, 0, 0, 0);
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;

            // Чистый белый лист заметок
            var (scroll, content) = UITheme.CreateVerticalScrollView(root, "NotesScroll", UITheme.LightPaperBg, 4f, 10);
            UITheme.AddLayout(scroll, flexHeight: 1);
            _listContent = content;

            var sep = UITheme.CreatePanel(root, "Sep", UITheme.LightDivider);
            UITheme.AddLayout(sep, prefHeight: 1);

            // Нижняя строка ввода (как на референсе: "Enter note...")
            var inputBar = UITheme.CreatePanel(root, "InputBar", UITheme.LightWindowBg);
            UITheme.AddLayout(inputBar, prefHeight: 42);
            var h = inputBar.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 6;
            h.padding = new RectOffset(6, 6, 5, 5);
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandHeight = true;
            h.childForceExpandWidth = false;

            _noteInput = UITheme.CreateInputField(inputBar, "NoteInput", "Enter note...", 15f, lightStyle: true, monospace: false);
            UITheme.AddLayout(_noteInput, flexWidth: 1);
            _noteInput.onSubmit.AddListener(OnSubmitNote);

            var addBtn = UITheme.CreateButton(inputBar, "AddBtn", "+", UITheme.WindowOrange, UITheme.TextPrimary, 18f, () =>
            {
                OnSubmitNote(_noteInput.text);
            });
            UITheme.AddLayout(addBtn, prefWidth: 34);

            Refresh();
        }

        private void OnSubmitNote(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            _notes.Add(text.Trim());
            _noteInput.text = string.Empty;
            _noteInput.ActivateInputField();
            AudioFeedbackManager.Instance?.PlayClick();
            Refresh();
        }

        public void AddNoteExternal(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            _notes.Add(text.Trim());
            Refresh();
        }

        private void Refresh()
        {
            if (_listContent == null) return;
            UITheme.ClearChildren(_listContent);

            for (int i = 0; i < _notes.Count; i++)
            {
                int indexCopy = i;
                var row = UITheme.CreatePanel(_listContent, "Note_" + i, new Color(0, 0, 0, 0));
                UITheme.AddLayout(row, prefHeight: 28);
                var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
                h.spacing = 6;
                h.childControlWidth = true;
                h.childControlHeight = true;
                h.childForceExpandHeight = true;
                h.childForceExpandWidth = false;

                var txt = UITheme.CreateText(row, "Txt", _notes[i], 15f, UITheme.TextDark, TextAlignmentOptions.MidlineLeft, FontStyles.Normal, monospace: true);
                UITheme.AddLayout(txt, flexWidth: 1);

                var delBtn = UITheme.CreateButton(row, "Del", "×", new Color(0, 0, 0, 0), UITheme.TextDarkMuted, 15f, () =>
                {
                    if (indexCopy >= 0 && indexCopy < _notes.Count)
                    {
                        _notes.RemoveAt(indexCopy);
                        Refresh();
                    }
                });
                UITheme.AddLayout(delBtn, prefWidth: 24);
            }
        }
    }
}
