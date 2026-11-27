using System;
using UnityEngine;
using Project.LinuxOS;

namespace Project.LarpCore
{
    public enum Day1Stage
    {
        MorningPrepare,            // 08:00 — проснулся, подготовиться к 1-му рабочему дню
        GoToWork,                  // 08:25 — пойти к двери на работу
        OfficeFirstDay,            // 09:00–17:45 — первый день в офисе (инфа о компании, графике и правилах)
        EveningConsoleWaitDoorbell,// 18:00 — дома у телека/приставки, через 2.5с звонок в дверь
        AnswerDoorForRouter,       // 18:05 — открыть дверь, забрать первый роутер
        PlaceRouter,               // 18:10 — механика 3D-плейсмента роутера в комнате
        GoToPCAndCheckMail,        // 19:00 — сесть за ПК, подключить Wi-Fi, прочесть ошибочное письмо об архиве
        TutorialSiteAndDecoder,    // 19:45 — первый сайт (анализ HTML, поиск, декодирование ключа)
        TutorialFatigueEnergyDrink,// 21:00 — ОБЯЗАТЕЛЬНО: усталость! Встать от ПК и выпить энергетик в комнате
        TutorialTraceProxy,        // 22:15 — ОБЯЗАТЕЛЬНО: Trace! Сменить прокси/VPN в ОС
        TutorialBruteforceAndParse,// 22:45 — доделать первый взлом и дождаться парсинга первых секретных данных
        RevelationAndNpcDialogue,  // 23:30 — озарение + диалоги с Мией и Маркусом (3 пути прохождения)
        FreeRoam                   // 00:00 — время ставится напрямую на 0:00, свободное плавание!
    }

    public class StorySlideData
    {
        public string Speaker;
        public string TimeBadge;
        public string Title;
        public string Body;
        public string ButtonLabel;
    }

    /// <summary>
    /// Управляет полным сценарием 1-го дня и обучением по документу:
    /// 08:00 утро -> офис -> 18:00 приставка и звонок в дверь -> плейсмент роутера ->
    /// ПК и ошибочное письмо -> первый сайт/декодер -> принудительный перерыв на энергетик (Усталость) ->
    /// смена прокси (Trace) -> первые секретные данные + диалоги с Мией и Маркусом -> 00:00 свободное плавание.
    /// </summary>
    public class Day1StoryController : MonoBehaviour
    {
        public static Day1StoryController Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Instance = null;

        public Day1Stage Stage { get; private set; } = Day1Stage.MorningPrepare;
        public bool IsTutorialActive => Stage != Day1Stage.FreeRoam;

        public event Action<Day1Stage> OnStageChanged;
        public event Action<StorySlideData[], Action> OnStorySequenceRequested;

        private RoomPropInteractable _wardrobeProp;
        private RoomPropInteractable _doorProp;
        private RoomPropInteractable _energyDrinkProp;
        private GameObject _energyDrinkVisual;
        private GameObject _tvConsoleVisual;

        private float _doorbellTimer = -1f;
        private bool _propsInitialized;

        public static Day1StoryController EnsureInstance()
        {
            if (Instance != null) return Instance;
            var existing = FindAnyObjectByType<Day1StoryController>();
            if (existing != null)
            {
                Instance = existing;
                return existing;
            }
            var go = new GameObject("Day1StoryController");
            return go.AddComponent<Day1StoryController>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void Start()
        {
            SetupRoomStoryProps();
        }

        private void Update()
        {
            if (Stage == Day1Stage.EveningConsoleWaitDoorbell && _doorbellTimer > 0f)
            {
                if (GameManager.Instance != null && GameManager.Instance.IsModalOpen)
                    return;

                _doorbellTimer -= Time.deltaTime;
                if (_doorbellTimer <= 0f)
                {
                    AudioFeedbackManager.Instance?.PlayDoorbell();
                    SetStage(Day1Stage.AnswerDoorForRouter);
                    GameManager.Instance?.PostToast("Звонок в дверь! Подойдите ко входу, чтобы забрать доставку роутера.");
                }
            }
        }

        public void StartDay1FromBeginning()
        {
            SetupRoomStoryProps();
            RouterPlacementController.EnsureInstance().ResetPlacement();

            var ps = PlayerState.EnsureInstance();
            ps.SetProxyNode("Direct (Home IP)");
            ps.SetTimeDirect(1, 8, 0, ticking: false);

            // Возвращаем персонажа на точку спавна в комнате
            var playerMove = FindAnyObjectByType<Move>();
            if (playerMove != null)
            {
                playerMove.StopMovement();
                playerMove.transform.position = new Vector3(15.7f, 0f, 7.7f);
            }

            if (PlayerStateController.Instance != null && PlayerStateController.Instance.IsAtComputer)
                PlayerStateController.Instance.ExitComputer();

            PlayerStateController.Instance?.MonitorPower?.TurnOff();

            SetStage(Day1Stage.MorningPrepare);
            GameManager.Instance?.PostToast("08:00 — Первый день в L.A.R.P. Corp. Подготовьтесь у шкафа и выходите на работу.");
        }

        private void SetupRoomStoryProps()
        {
            if (_propsInitialized) return;
            _propsInitialized = true;

            GameObject flatGo = GameObject.Find("Flat");
            if (flatGo == null) return;

            // 1. Шкаф в Комнате 2 (подготовка к первому рабочему дню)
            Transform wardrobe = flatGo.transform.Find("Комната2_10x6/Шкаф");
            if (wardrobe != null)
            {
                _wardrobeProp = wardrobe.gameObject.GetComponent<RoomPropInteractable>() ?? wardrobe.gameObject.AddComponent<RoomPropInteractable>();
                _wardrobeProp.Configure("wardrobe", "Подготовиться к работе (взять пропуск и худи)", 3.2f, OnWardrobeUsed, new Vector3(18.0f, 0f, 12.35f));
            }

            // 2. Входная дверь (x: 20.0, z: 4.75 — рядом с Подпись_ВХОД)
            var doorGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            doorGo.name = "FrontDoorInteractable";
            doorGo.transform.position = new Vector3(19.85f, 1.1f, 4.75f);
            doorGo.transform.localScale = new Vector3(0.22f, 2.2f, 1.5f);
            Shader urpLit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var doorMat = new Material(urpLit);
            Color doorColor = new Color(0.24f, 0.17f, 0.12f, 1f);
            if (doorMat.HasProperty("_BaseColor")) doorMat.SetColor("_BaseColor", doorColor);
            else if (doorMat.HasProperty("_Color")) doorMat.SetColor("_Color", doorColor);
            doorGo.GetComponent<Renderer>().sharedMaterial = doorMat;

            _doorProp = doorGo.AddComponent<RoomPropInteractable>();
            _doorProp.Configure("front_door", "Выйти на работу в L.A.R.P. Corp.", 3.2f, OnFrontDoorUsed, new Vector3(18.5f, 0f, 4.75f));

            // 3. Физическая банка энергетика на обеденном столе в Комнате 3 (x: 13.65, y: 0.76, z: 4.0)
            _energyDrinkVisual = CreateEnergyDrinkVisual();
            _energyDrinkVisual.transform.position = new Vector3(13.65f, 0.76f, 4.0f);
            _energyDrinkProp = _energyDrinkVisual.AddComponent<RoomPropInteractable>();
            _energyDrinkProp.Configure("energy_drink", "Выпить энергетик (-50% Усталости)", 3.0f, OnEnergyDrinkUsed, new Vector3(14.8f, 0f, 4.0f));

            // 4. Телевизор + приставка у дивана в Комнате 3 (для сцены в 18:00)
            _tvConsoleVisual = CreateTvConsoleVisual();
            _tvConsoleVisual.transform.position = new Vector3(15.7f, 0.465f, 1.25f);
        }

        private void RefreshPropsForStage()
        {
            if (_wardrobeProp != null)
            {
                _wardrobeProp.IsInteractionEnabled = Stage == Day1Stage.MorningPrepare;
            }

            if (_doorProp != null)
            {
                if (Stage == Day1Stage.MorningPrepare || Stage == Day1Stage.GoToWork)
                {
                    _doorProp.IsInteractionEnabled = true;
                    _doorProp.PromptLabel = "Отправиться на первый рабочий день в офис";
                }
                else if (Stage == Day1Stage.AnswerDoorForRouter)
                {
                    _doorProp.IsInteractionEnabled = true;
                    _doorProp.PromptLabel = "Открыть дверь курьеру (забрать первый роутер)";
                }
                else
                {
                    _doorProp.IsInteractionEnabled = false;
                }
            }

            if (_energyDrinkProp != null)
            {
                bool drinkAvailable = Stage == Day1Stage.TutorialFatigueEnergyDrink || Stage == Day1Stage.FreeRoam;
                _energyDrinkProp.IsInteractionEnabled = drinkAvailable;
                if (_energyDrinkVisual != null) _energyDrinkVisual.SetActive(true);
            }
        }

        private void SetStage(Day1Stage newStage)
        {
            Stage = newStage;
            RefreshPropsForStage();
            OnStageChanged?.Invoke(newStage);
            GameManager.Instance?.NotifyChanged();
        }

        // === СОБЫТИЯ СЮЖЕТА ДНЯ 1 ===

        private void OnWardrobeUsed()
        {
            if (Stage != Day1Stage.MorningPrepare) return;
            PlayerState.Instance?.SetTimeDirect(1, 8, 25, false);
            AudioFeedbackManager.Instance?.PlayDecodeSuccess();
            GameManager.Instance?.PostToast("Пропуск и вещи собраны! Подойдите к входной двери, чтобы пойти на работу.");
            SetStage(Day1Stage.GoToWork);
        }

        private void OnFrontDoorUsed()
        {
            if (Stage == Day1Stage.MorningPrepare || Stage == Day1Stage.GoToWork)
            {
                StartOfficeSequence();
            }
            else if (Stage == Day1Stage.AnswerDoorForRouter)
            {
                // Получаем первый роутер и запускаем механику 3D-плейсмента!
                PlayerState.Instance?.SetTimeDirect(1, 18, 10, false);
                SetStage(Day1Stage.PlaceRouter);
                RouterPlacementController.EnsureInstance().BeginPlacement(OnRouterPlaced);
            }
        }

        private void StartOfficeSequence()
        {
            SetStage(Day1Stage.OfficeFirstDay);
            PlayerState.Instance?.SetTimeDirect(1, 9, 0, false);

            var slides = new[]
            {
                new StorySlideData
                {
                    Speaker = "ОФИС L.A.R.P. CORP.  —  ДЕНЬ 1",
                    TimeBadge = "09:00 AM",
                    Title = "Вводный брифинг нового сотрудника",
                    Body =
                        "Вы прибыли в штаб-квартиру L.A.R.P. Corp. — крупнейшей корпорации анализа данных.\n\n" +
                        "Шеф СБ Маркус Стоун вручает вам пропуск младшего аналитика:\n" +
                        "«Рабочий день с 09:00 до 18:00. Опаздываешь или проспишь из-за недосыпа — списываем штраф из зарплаты. Рабочие тикеты приходят на твой домашний терминал.»",
                    ButtonLabel = "ДАЛЕЕ (ИНСТРУКТАЖ ОТДЕЛА)"
                },
                new StorySlideData
                {
                    Speaker = "ОТДЕЛ АНАЛИТИКИ L.A.R.P.",
                    TimeBadge = "01:30 PM",
                    Title = "Внутренняя сеть и правила допуска",
                    Body =
                        "Вам показывают рабочие отчёты, корпоративный интранет и структуру отделов.\n\n" +
                        "Руководитель напоминает: доступ к закрытым серверам телеметрии строго запрещён, а все подозрительные подключения фиксирует система трассировки СБ (Trace).",
                    ButtonLabel = "ЗАВЕРШИТЬ СМЕНУ (18:00)"
                },
                new StorySlideData
                {
                    Speaker = "ВОЗВРАЩЕНИЕ ДОМОЙ",
                    TimeBadge = "06:00 PM",
                    Title = "Вечер в квартире",
                    Body =
                        "Первая смена позади. В 18:00 вы уже дома — устроились у дивана с приставкой перед телевизором, чтобы выдохнуть после офиса.\n\n" +
                        "Сегодня вечером курьер должен привезти ваш первый домашний роутер...",
                    ButtonLabel = "ВЕРНУТЬСЯ В КВАРТИРУ"
                }
            };

            OnStorySequenceRequested?.Invoke(slides, EnterEveningGamingScene);
        }

        private void EnterEveningGamingScene()
        {
            PlayerState.Instance?.SetTimeDirect(1, 18, 0, false);

            // Ставим героя у дивана перед телеком/приставкой в привычном изометрическом виде камеры
            var playerMove = FindAnyObjectByType<Move>();
            if (playerMove != null)
            {
                playerMove.StopMovement();
                playerMove.transform.position = new Vector3(14.5f, 0f, 2.3f);
            }

            SetStage(Day1Stage.EveningConsoleWaitDoorbell);
            _doorbellTimer = 2.6f;
            GameManager.Instance?.PostToast("18:00 — Вы дома у приставки. Отдыхаете после первого дня в офисе...");
        }

        private void OnRouterPlaced()
        {
            PlayerState.Instance?.SetTimeDirect(1, 19, 0, false);
            SetStage(Day1Stage.GoToPCAndCheckMail);
            GameManager.Instance?.PostToast("Роутер работает! Подойдите к компьютеру, включите его, подключите Wi-Fi и проверьте Mail.");
        }

        public void NotifyLeakMailOpened()
        {
            if (Stage != Day1Stage.GoToPCAndCheckMail) return;
            PlayerState.Instance?.SetTimeDirect(1, 19, 45, false);
            SetStage(Day1Stage.TutorialSiteAndDecoder);
            GameManager.Instance?.PostToast("Обучение: откройте сайт в Browser, изучите скрытый блок и </> Исходный код, затем декодируйте ключ в Decoder.");
        }

        public void NotifyFirstKeyDecoded()
        {
            if (Stage != Day1Stage.TutorialSiteAndDecoder) return;

            // ОБЯЗАТЕЛЬНЫЙ ЭТАП ИЗ 2-ГО ФОТО:
            // Посреди обучения резко поднимаем усталость и заставляем встать от ПК и выпить энергетик!
            PlayerState.Instance?.SetTimeDirect(1, 21, 0, false);
            GameManager.Instance?.SetFatigueDirect(85);
            SetStage(Day1Stage.TutorialFatigueEnergyDrink);
            AudioFeedbackManager.Instance?.PlayErrorBuzzer();
            GameManager.Instance?.PostToast("УСТАЛОСТЬ 85%! Вы слишком вымотаны для взлома. Встаньте из-за ПК (ESC) и выпейте энергетик на столе в гостиной!");
        }

        private void OnEnergyDrinkUsed()
        {
            var gm = GameManager.Instance;
            if (gm == null) return;

            AudioFeedbackManager.Instance?.PlayRefresh();

            if (Stage == Day1Stage.TutorialFatigueEnergyDrink)
            {
                gm.ReduceFatigue(50);
                PlayerState.Instance?.SetTimeDirect(1, 22, 15, false);

                // ОБЯЗАТЕЛЬНЫЙ ЭТАП ИЗ 2-ГО ФОТО:
                // Обучение механике Trace — заставляем вернуться к ПК и сменить прокси/VPN!
                gm.SetTraceDirect(45);
                SetStage(Day1Stage.TutorialTraceProxy);
                GameManager.Instance?.PostToast("Энергетик сработал (-50% усталости)! Вернитесь к ПК — СБ засекла прямой IP (Trace 45%), нужно срочно сменить Proxy!");
                return;
            }

            // В свободном режиме банкой на столе тоже можно взбодриться
            if (gm.Fatigue > 0)
            {
                gm.ReduceFatigue(35);
                gm.PostToast($"Выпит энергетик! Усталость снижена до {gm.Fatigue}%.");
            }
            else
            {
                gm.PostToast("Вы и так полны сил (усталость 0%).");
            }
        }

        public void NotifyProxySwitched()
        {
            if (Stage != Day1Stage.TutorialTraceProxy) return;

            PlayerState.Instance?.SetTimeDirect(1, 22, 45, false);
            SetStage(Day1Stage.TutorialBruteforceAndParse);
            GameManager.Instance?.PostToast("Отлично! Прокси активирован, Trace снижен. Теперь завершите Bruteforce в терминале и дождитесь парсинга!");
        }

        public void NotifyFirstArchiveParsed()
        {
            if (Stage != Day1Stage.TutorialBruteforceAndParse) return;

            PlayerState.Instance?.SetTimeDirect(1, 23, 30, false);
            SetStage(Day1Stage.RevelationAndNpcDialogue);

            var slides = new[]
            {
                new StorySlideData
                {
                    Speaker = "АЛЕКС «NULLPTR»  —  ОЗАРЕНИЕ",
                    TimeBadge = "11:30 PM",
                    Title = "Первые секретные данные L.A.R.P. Corp.",
                    Body =
                        "Вы перечитываете расшифрованный архив «Проект ОКО» в полном недоумении...\n\n" +
                        "Ваша корпорация тайно перехватывает трафик с сотен тысяч домашних роутеров и продаёт досье на граждан. " +
                        "L.A.R.P. Corp. совсем не чиста — и с этим нужно что-то делать.",
                    ButtonLabel = "ВХОДЯЩЕЕ СООБЩЕНИЕ (МИЯ)"
                },
                new StorySlideData
                {
                    Speaker = "МИЯ  —  НЕЗАВИСИМЫЙ ЖУРНАЛИСТ",
                    TimeBadge = "11:40 PM",
                    Title = "Пути «Досье» и «Блог»",
                    Body =
                        "«Привет, NullPtr. Я давно копаю под L.A.R.P. Corp. Если ты достал реальный компромат — у тебя есть выбор:\n" +
                        "• Передавай улики мне в ДОСЬЕ — соберём доказательства для суда и посадим верхушку.\n" +
                        "• Или отдавай сливы брокеру Призраку в анонимный БЛОГ — наберёшь фолловеров, заработаешь на донатах и обрушишь акции компании.»",
                    ButtonLabel = "ВХОДЯЩИЙ ЗВОНОК (ШЕФ СБ)"
                },
                new StorySlideData
                {
                    Speaker = "МАРКУС СТОУН  —  ШЕФ СБ L.A.R.P.",
                    TimeBadge = "11:52 PM",
                    Title = "Путь «Авторитет / Босс»",
                    Body =
                        "«Алекс, ты ещё не спишь? Наши датчики фиксировали активность на внутреннем контуре.\n" +
                        "Слушай внимательно: если вдруг наткнёшься на дыры в защите или чужие утечки — сразу сдавай их мне. " +
                        "Получишь премии, снятие подозрений СБ и быстрый карьерный рост внутри L.A.R.P.»",
                    ButtonLabel = "НАЧАТЬ СВОБОДНОЕ ПЛАВАНИЕ (00:00)"
                }
            };

            OnStorySequenceRequested?.Invoke(slides, EnterFreeRoamAtMidnight);
        }

        private void EnterFreeRoamAtMidnight()
        {
            // Время ставим напрямую на 0:00, т.к. обучение проходит за константу (как указано на 2-м фото!)
            PlayerState.Instance?.SetTimeDirect(1, 0, 0, ticking: true);
            SetStage(Day1Stage.FreeRoam);
            GameManager.Instance?.PostToast("00:00 — Обучение завершено! Теперь вы в свободном плавании: выберите путь в Дата-Центре или исследуйте ночь.");
        }

        public bool CanStartBruteforceNow(out string tutorialBlockReason)
        {
            tutorialBlockReason = string.Empty;
            if (Stage == Day1Stage.TutorialFatigueEnergyDrink)
            {
                tutorialBlockReason = "Слишком высокая усталость (85%)! Встаньте из-за ПК (ESC) и выпейте энергетик на обеденном столе в комнате.";
                return false;
            }
            if (Stage == Day1Stage.TutorialTraceProxy)
            {
                tutorialBlockReason = "ВНИМАНИЕ! Trace 45% на прямом IP! Сначала откройте «Proxy / VPN» на рабочем столе и смените прокси.";
                return false;
            }
            return true;
        }

        public string GetStageObjectiveText()
        {
            switch (Stage)
            {
                case Day1Stage.MorningPrepare:
                    return "08:00 — Первый день в L.A.R.P. Corp. Подготовьтесь у шкафа (в комнате справа) или сразу идите к входной двери.";
                case Day1Stage.GoToWork:
                    return "08:25 — Вещи готовы. Подойдите к входной двери (ВХОД ->), чтобы пойти в офис на первый рабочий день.";
                case Day1Stage.OfficeFirstDay:
                    return "Первый рабочий день в офисе L.A.R.P. Corp...";
                case Day1Stage.EveningConsoleWaitDoorbell:
                    return "18:00 — Вечер дома у приставки. Отдых после смены...";
                case Day1Stage.AnswerDoorForRouter:
                    return "18:05 — Звонок в дверь! Подойдите ко входу (ВХОД ->), чтобы забрать свой первый роутер.";
                case Day1Stage.PlaceRouter:
                    return "18:10 — Разместите роутер в квартире: наведите на стол/пол, [R] повернуть, [ЛКМ] поставить.";
                case Day1Stage.GoToPCAndCheckMail:
                    return "19:00 — Подойдите к ПК, включите его, подключите открытый Wi-Fi в трее и откройте ошибочное письмо в Mail.";
                case Day1Stage.TutorialSiteAndDecoder:
                    return "19:45 — Анализ первого сайта: найдите 3 части ключа в Browser (скрытый блок и </> Исходный код) и расшифруйте их в Decoder.";
                case Day1Stage.TutorialFatigueEnergyDrink:
                    return "21:00 — УСТАЛОСТЬ 85%! Встаньте от ПК (ESC) и выпейте энергетик на обеденном столе в гостиной!";
                case Day1Stage.TutorialTraceProxy:
                    return "22:15 — TRACE 45%! Сядьте за ПК, откройте «Proxy / VPN» на рабочем столе и смените прокси-маршрут!";
                case Day1Stage.TutorialBruteforceAndParse:
                    return "22:45 — Откройте Bruteforce, вставьте ключ и пройдите обход защиты, затем дождитесь парсинга в Data Center.";
                case Day1Stage.RevelationAndNpcDialogue:
                    return "23:30 — Секретные данные получены. Просмотрите диалог с Мией и Маркусом.";
                default:
                    return GameManager.Instance != null ? GameManager.Instance.GetCurrentObjectiveHint() : "";
            }
        }

        private static GameObject CreateEnergyDrinkVisual()
        {
            var root = new GameObject("EnergyDrink_Can");
            Shader urpLit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

            Material MakeMat(Color c)
            {
                var m = new Material(urpLit);
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
                else if (m.HasProperty("_Color")) m.SetColor("_Color", c);
                return m;
            }

            // Банка энергетика (хорошо заметная, с яркой оранжево-лаймовой полосой)
            var body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            body.name = "CanBody";
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0f, 0.14f, 0f);
            body.transform.localScale = new Vector3(0.14f, 0.14f, 0.14f);
            body.GetComponent<Renderer>().sharedMaterial = MakeMat(new Color(0.12f, 0.14f, 0.16f, 1f));

            var stripe = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            stripe.name = "CanStripe";
            stripe.transform.SetParent(root.transform, false);
            stripe.transform.localPosition = new Vector3(0f, 0.15f, 0f);
            stripe.transform.localScale = new Vector3(0.146f, 0.055f, 0.146f);
            stripe.GetComponent<Renderer>().sharedMaterial = MakeMat(new Color(0.88f, 0.45f, 0.14f, 1f));
            Destroy(stripe.GetComponent<Collider>());

            // Удобный невидимый триггер-коллайдер побольше, чтобы легко кликать по банке в изометрии
            var clickBox = root.AddComponent<BoxCollider>();
            clickBox.center = new Vector3(0f, 0.15f, 0f);
            clickBox.size = new Vector3(0.55f, 0.45f, 0.55f);

            return root;
        }

        private static GameObject CreateTvConsoleVisual()
        {
            var root = new GameObject("TV_And_Console");
            Shader urpLit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

            Material MakeMat(Color c)
            {
                var m = new Material(urpLit);
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
                else if (m.HasProperty("_Color")) m.SetColor("_Color", c);
                return m;
            }

            void Box(string n, Vector3 pos, Vector3 scl, Color c)
            {
                var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
                g.name = n;
                g.transform.SetParent(root.transform, false);
                g.transform.localPosition = pos;
                g.transform.localScale = scl;
                g.GetComponent<Renderer>().sharedMaterial = MakeMat(c);
                Destroy(g.GetComponent<Collider>());
            }

            // Игровая приставка и геймпад на журнальном столике
            Box("ConsoleBody", new Vector3(-0.22f, 0.04f, 0f), new Vector3(0.36f, 0.08f, 0.28f), new Color(0.12f, 0.13f, 0.15f, 1f));
            Box("ConsoleLed", new Vector3(-0.22f, 0.045f, 0.142f), new Vector3(0.24f, 0.015f, 0.01f), new Color(0.20f, 0.65f, 0.95f, 1f));
            Box("Gamepad", new Vector3(0.18f, 0.03f, 0.06f), new Vector3(0.18f, 0.05f, 0.11f), new Color(0.22f, 0.24f, 0.27f, 1f));
            // Небольшой ТВ-экран на краю столика
            Box("TvStand", new Vector3(0f, 0.04f, -0.30f), new Vector3(0.32f, 0.04f, 0.16f), new Color(0.15f, 0.16f, 0.18f, 1f));
            Box("TvScreen", new Vector3(0f, 0.34f, -0.32f), new Vector3(0.95f, 0.54f, 0.05f), new Color(0.08f, 0.10f, 0.14f, 1f));
            Box("TvGlow", new Vector3(0f, 0.34f, -0.292f), new Vector3(0.88f, 0.47f, 0.01f), new Color(0.18f, 0.34f, 0.48f, 1f));

            return root;
        }
    }
}
