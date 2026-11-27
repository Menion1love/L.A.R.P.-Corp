using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Project.LarpCore
{
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Instance = null;

        public int Day { get; private set; } = 1;
        public int Money { get; private set; } = 150;
        public int Fatigue { get; private set; } = 0;       // 0..100
        public int Trace { get; private set; } = 0;         // 0..100
        public int Authority { get; private set; } = 10;    // Авторитет
        public int Followers { get; private set; } = 0;     // Фолловеры
        public int Dossier { get; private set; } = 0;       // Досье
        public int DataCenterLevel { get; private set; } = 1; // 1..3
        public bool HasPrinter3D { get; private set; } = false;

        public int HacksCompletedToday { get; private set; }
        public int ExtraEarnedToday { get; private set; }
        public int SpentToday { get; private set; }
        public bool IsGameOver { get; private set; }
        public string GameOverReason { get; private set; }
        public bool IsModalOpen { get; set; }
        public bool IntroDismissed { get; set; }
        public string LastToastMessage { get; private set; }
        public float LastToastTime { get; private set; }

        public List<HackServerData> Servers { get; private set; } = new List<HackServerData>();
        public List<KeyFragmentData> Fragments { get; private set; } = new List<KeyFragmentData>();
        public List<ServicePageData> Pages { get; private set; } = new List<ServicePageData>();
        public List<MailMessageData> Mail { get; private set; } = new List<MailMessageData>();
        public List<FreelanceJobData> FreelanceJobs { get; private set; } = new List<FreelanceJobData>();
        public List<ShopItemData> ShopItems { get; private set; } = new List<ShopItemData>();
        public List<ActiveParseJob> ParseJobs { get; private set; } = new List<ActiveParseJob>();

        private readonly List<MoralBranch> _decisionsToday = new List<MoralBranch>();

        public event Action OnStateChanged;
        public event Action<string> OnToast;
        public event Action<DaySummaryReport> OnDaySummaryReady;
        public event Action<string> OnGameOverTriggered;
        public event Action<string> OnOpenBrowserUrlRequested;

        public static GameManager EnsureInstance()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("GameManager");
            return go.AddComponent<GameManager>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            AudioFeedbackManager.EnsureInstance();
            InitializeNewGame();
        }

        public void InitializeNewGame()
        {
            Day = 1;
            Money = 150;
            Fatigue = 0;
            Trace = 0;
            Authority = 10;
            Followers = 0;
            Dossier = 0;
            DataCenterLevel = 1;
            HasPrinter3D = false;
            HacksCompletedToday = 0;
            ExtraEarnedToday = 0;
            SpentToday = 0;
            IsGameOver = false;
            GameOverReason = string.Empty;
            IsModalOpen = false;
            _decisionsToday.Clear();

            Servers = Day1Content.CreateServers();
            Fragments = Day1Content.CreateKeyFragments();
            Pages = Day1Content.CreateServicePages();
            Mail = Day1Content.CreateDay1Mail();
            FreelanceJobs = Day1Content.CreateFreelanceJobs();
            ShopItems = Day1Content.CreateShopItems();
            ParseJobs = new List<ActiveParseJob>();

            NotifyChanged();
        }

        private void Update()
        {
            if (IsGameOver || ParseJobs.Count == 0)
                return;

            bool anyCompletedNow = false;
            bool anyActive = false;

            for (int i = 0; i < ParseJobs.Count; i++)
            {
                var job = ParseJobs[i];
                if (job.Completed) continue;

                anyActive = true;
                job.Elapsed += Time.deltaTime;
                if (job.Elapsed >= job.Duration)
                {
                    job.Elapsed = job.Duration;
                    job.Completed = true;
                    anyCompletedNow = true;

                    var srv = Servers.FirstOrDefault(s => s.Id == job.ServerId);
                    if (srv != null)
                    {
                        srv.IsParsing = false;
                        srv.IsParsed = true;
                    }

                    AudioFeedbackManager.Instance?.PlayNotification();
                    PostToast($"Дата-Центр завершил парсинг: {job.ServerName}! Откройте вкладку «Дата-Центр».");
                    if (job.ServerId == "srv_eye_01")
                    {
                        Day1StoryController.Instance?.NotifyFirstArchiveParsed();
                    }
                }
            }

            if (anyCompletedNow)
            {
                NotifyChanged();
            }
            else if (anyActive)
            {
                // UI polling reads Progress smoothly without spamming full rebuilds
            }
        }

        public float GetParseSpeedMultiplier()
        {
            switch (DataCenterLevel)
            {
                case 1: return 1f;
                case 2: return 2f;
                default: return 4f;
            }
        }

        public int GetUnreadMailCount()
        {
            return Mail.Count(m => m.DayReceived <= Day && !m.IsRead);
        }

        public int GetReadyEvidenceCount()
        {
            return Servers.Count(s => s.IsParsed && !s.IsDecided);
        }

        public int GetActiveParseCount()
        {
            return ParseJobs.Count(p => !p.Completed);
        }

        public void PostToast(string message)
        {
            LastToastMessage = message;
            LastToastTime = Time.unscaledTime;
            OnToast?.Invoke(message);
        }

        public void RequestOpenUrlInBrowser(string url)
        {
            OnOpenBrowserUrlRequested?.Invoke(url);
        }

        public void MarkMailRead(string mailId)
        {
            var m = Mail.FirstOrDefault(x => x.Id == mailId);
            if (m != null && !m.IsRead)
            {
                m.IsRead = true;
                NotifyChanged();
            }

            if (mailId == "mail_leak_accident")
            {
                Day1StoryController.Instance?.NotifyLeakMailOpened();
            }
        }

        public bool CompleteMailTicket(string mailId)
        {
            var m = Mail.FirstOrDefault(x => x.Id == mailId);
            if (m == null || !m.HasTicketAction || m.TicketCompleted)
                return false;

            m.TicketCompleted = true;
            m.IsRead = true;
            Money += m.TicketRewardMoney;
            ExtraEarnedToday += m.TicketRewardMoney;
            Authority += m.TicketRewardAuthority;
            Fatigue = Mathf.Clamp(Fatigue + m.TicketFatigueCost, 0, 100);

            AudioFeedbackManager.Instance?.PlayDecodeSuccess();
            PostToast($"Тикет закрыт! +${m.TicketRewardMoney}, +{m.TicketRewardAuthority} Авторитет.");
            NotifyChanged();
            return true;
        }

        public bool DiscoverFragment(string fragmentId)
        {
            var frag = Fragments.FirstOrDefault(f => f.Id == fragmentId);
            if (frag == null || frag.IsFound)
                return false;

            frag.IsFound = true;
            if (!frag.NeedsDecoding)
                frag.IsDecoded = true;

            AudioFeedbackManager.Instance?.PlayClueFound();
            PostToast(frag.NeedsDecoding
                ? $"Найден фрагмент #{frag.SlotIndex} ({frag.RawValue})! Расшифруйте его в «Декодере»."
                : $"Найден готовый фрагмент #{frag.SlotIndex}: {frag.DecodedValue}!");
            NotifyChanged();
            return true;
        }

        public bool TryDecodeFragment(string fragmentId, KeyEncoding chosenEncoding, out string outputText)
        {
            outputText = string.Empty;
            var frag = Fragments.FirstOrDefault(f => f.Id == fragmentId);
            if (frag == null || !frag.IsFound)
            {
                outputText = "Фрагмент не найден.";
                return false;
            }

            if (frag.IsDecoded)
            {
                outputText = frag.DecodedValue;
                return true;
            }

            outputText = KeyDecoderUtil.Decode(frag.RawValue, chosenEncoding);
            if (chosenEncoding == frag.Encoding && string.Equals(outputText, frag.DecodedValue, StringComparison.OrdinalIgnoreCase))
            {
                frag.IsDecoded = true;
                AudioFeedbackManager.Instance?.PlayDecodeSuccess();

                if (IsServerKeyComplete(frag.TargetServerId))
                {
                    PostToast($"Ключ собран полностью ({GetAssembledKey(frag.TargetServerId)})! Переходите в Bruteforce.");
                    if (frag.TargetServerId == "srv_eye_01")
                    {
                        Day1StoryController.Instance?.NotifyFirstKeyDecoded();
                    }
                }
                else
                {
                    PostToast($"Фрагмент #{frag.SlotIndex} успешно декодирован: {frag.DecodedValue}");
                }

                NotifyChanged();
                return true;
            }

            AudioFeedbackManager.Instance?.PlayErrorBuzzer();
            PostToast($"Неверный алгоритм ({KeyDecoderUtil.GetEncodingLabel(chosenEncoding)}). Попробуйте другой формат.");
            return false;
        }

        public List<KeyFragmentData> GetFragmentsForServer(string serverId)
        {
            return Fragments.Where(f => f.TargetServerId == serverId).OrderBy(f => f.SlotIndex).ToList();
        }

        public bool IsServerKeyComplete(string serverId)
        {
            var frags = GetFragmentsForServer(serverId);
            return frags.Count > 0 && frags.All(f => f.IsFound && f.IsDecoded);
        }

        public string GetAssembledKey(string serverId)
        {
            var frags = GetFragmentsForServer(serverId);
            return string.Join("-", frags.Select(f => (f.IsFound && f.IsDecoded) ? f.DecodedValue : "????"));
        }

        public bool CanStartHack(HackServerData server, out string blockReason)
        {
            blockReason = string.Empty;
            if (server == null)
            {
                blockReason = "Сервер не выбран.";
                return false;
            }
            if (server.IsHacked)
            {
                blockReason = "Этот сервер уже взломан.";
                return false;
            }
            if (Fatigue >= 100)
            {
                blockReason = "Герой полностью вымотан (100%). Выпейте кофе в DarkNet или лягте спать!";
                return false;
            }
            return true;
        }

        public void AddTrace(int delta, string reason)
        {
            if (IsGameOver) return;

            Trace = Mathf.Clamp(Trace + delta, 0, 100);
            if (delta > 0)
            {
                AudioFeedbackManager.Instance?.PlayErrorBuzzer();
                PostToast($"ТРЕВОГА! Trace +{delta}% ({reason}) -> {Trace}%");
            }

            if (Trace >= 100)
            {
                TriggerGameOver("Уровень цифрового следа (Trace) достиг 100%!\nСлужба безопасности L.A.R.P. Corp. вычислила ваш IP и провела обыск.");
                return;
            }

            NotifyChanged();
        }

        public void SetFatigueDirect(int value)
        {
            Fatigue = Mathf.Clamp(value, 0, 100);
            NotifyChanged();
        }

        public void ReduceFatigue(int amount)
        {
            Fatigue = Mathf.Clamp(Fatigue - Mathf.Max(0, amount), 0, 100);
            NotifyChanged();
        }

        public void SetTraceDirect(int value)
        {
            Trace = Mathf.Clamp(value, 0, 99);
            NotifyChanged();
        }

        public void ReduceTrace(int amount, string reason)
        {
            Trace = Mathf.Clamp(Trace - Mathf.Max(0, amount), 0, 100);
            AudioFeedbackManager.Instance?.PlayDecodeSuccess();
            PostToast($"{reason}: Trace снижен до {Trace}%.");
            NotifyChanged();
        }

        public void SpendMoney(int amount)
        {
            if (amount <= 0) return;
            Money -= amount;
            SpentToday += amount;
            NotifyChanged();
        }

        public void CompleteHackAndStartParsing(string serverId)
        {
            var srv = Servers.FirstOrDefault(s => s.Id == serverId);
            if (srv == null || srv.IsHacked) return;

            srv.IsHacked = true;
            srv.IsParsing = true;
            HacksCompletedToday++;
            Fatigue = Mathf.Clamp(Fatigue + srv.FatigueCost, 0, 100);

            float duration = Mathf.Max(2.5f, srv.BaseParseSeconds / GetParseSpeedMultiplier());
            ParseJobs.Add(new ActiveParseJob
            {
                ServerId = srv.Id,
                ServerName = srv.DisplayName,
                Elapsed = 0f,
                Duration = duration,
                Completed = false
            });

            AudioFeedbackManager.Instance?.PlayDownloadComplete();
            PostToast($"Взлом завершён! Сырые данные отправлены в Дата-Центр (парсинг ~{Mathf.CeilToInt(duration)} сек). Усталость: {Fatigue}%.");
            NotifyChanged();
        }

        public bool ApplyMoralChoice(string serverId, MoralBranch branch)
        {
            var srv = Servers.FirstOrDefault(s => s.Id == serverId);
            if (srv == null || !srv.IsParsed || srv.IsDecided)
                return false;

            srv.IsDecided = true;
            srv.ChosenBranch = branch;
            _decisionsToday.Add(branch);

            switch (branch)
            {
                case MoralBranch.Dossier:
                    Dossier += 25;
                    PostToast($"Компромат передан Мие в ДОСЬЕ! (+25 Досье). Дело против руководства крепнет.");
                    break;

                case MoralBranch.Blog:
                    Followers += 25;
                    Money += 90;
                    ExtraEarnedToday += 90;
                    Trace = Mathf.Clamp(Trace + 5, 0, 99);
                    PostToast($"Слив опубликован в БЛОГЕ! (+25 Фолловеры, +$90 донаты, +5% Trace). Акции L.A.R.P. падают!");
                    break;

                case MoralBranch.Boss:
                    Authority += 25;
                    Money += 110;
                    ExtraEarnedToday += 110;
                    Trace = Mathf.Clamp(Trace - 15, 0, 100);
                    PostToast($"Уязвимость сдана Маркусу! (+25 Авторитет, +$110 премия, -15% Trace).");
                    break;
            }

            AudioFeedbackManager.Instance?.PlayDecodeSuccess();
            NotifyChanged();
            return true;
        }

        public bool CompleteFreelanceJob(string jobId, out string failReason)
        {
            failReason = string.Empty;
            var job = FreelanceJobs.FirstOrDefault(j => j.Id == jobId);
            if (job == null || job.IsCompleted)
            {
                failReason = "Заказ уже выполнен.";
                return false;
            }
            if (job.RequiresPrinter3D && !HasPrinter3D)
            {
                failReason = "Требуется 3D-принтер (купите в DarkNet-магазине).";
                return false;
            }
            if (Fatigue + job.FatigueCost > 100)
            {
                failReason = "Слишком высокая усталость! Выпейте кофе или завершите день.";
                return false;
            }

            job.IsCompleted = true;
            Money += job.RewardMoney;
            ExtraEarnedToday += job.RewardMoney;
            Fatigue = Mathf.Clamp(Fatigue + job.FatigueCost, 0, 100);

            AudioFeedbackManager.Instance?.PlayDecodeSuccess();
            PostToast($"Заказ «{job.Title}» выполнен! +${job.RewardMoney} (Усталость {Fatigue}%).");
            NotifyChanged();
            return true;
        }

        public bool PurchaseShopItem(string itemId, out string failReason)
        {
            failReason = string.Empty;
            var item = ShopItems.FirstOrDefault(i => i.Id == itemId);
            if (item == null)
            {
                failReason = "Товар не найден.";
                return false;
            }
            if (!item.Repeatable && item.Purchased)
            {
                failReason = "Уже куплено.";
                return false;
            }
            if (item.Id == "upg_dc_3" && DataCenterLevel < 2)
            {
                failReason = "Сначала купите Апгрейд Дата-Центра → Ур. 2.";
                return false;
            }
            if (Money < item.Cost)
            {
                failReason = $"Недостаточно средств (нужно ${item.Cost}).";
                return false;
            }

            Money -= item.Cost;
            SpentToday += item.Cost;
            if (!item.Repeatable) item.Purchased = true;

            switch (item.Type)
            {
                case UpgradeType.DataCenterUpgrade:
                    DataCenterLevel = Mathf.Min(3, DataCenterLevel + 1);
                    // Ускоряем и уже идущие задачи парсинга!
                    foreach (var p in ParseJobs)
                    {
                        if (!p.Completed)
                            p.Duration = Mathf.Max(p.Elapsed + 1.5f, p.Duration * 0.55f);
                    }
                    PostToast($"Домашний Дата-Центр улучшен до Ур. {DataCenterLevel}! Скорость парсинга x{GetParseSpeedMultiplier():0}.");
                    break;

                case UpgradeType.Printer3D:
                    HasPrinter3D = true;
                    PostToast("3D-Принтер установлен в убежище! Открыты заказы на печать устройств.");
                    break;

                case UpgradeType.ClearLogsVpn:
                    Trace = Mathf.Clamp(Trace - 25, 0, 100);
                    PostToast($"Логи очищены, IP сменён! Уровень Trace снижен до {Trace}%.");
                    break;

                case UpgradeType.CoffeeEnergy:
                    Fatigue = Mathf.Clamp(Fatigue - 25, 0, 100);
                    PostToast($"Двойной эспрессо выпит! Усталость снижена до {Fatigue}%.");
                    break;
            }

            AudioFeedbackManager.Instance?.PlayDecodeSuccess();
            NotifyChanged();
            return true;
        }

        public DaySummaryReport SleepAndEndDay()
        {
            if (IsGameOver) return null;

            // Допарсиваем незавершённые архивы за ночь? Нет, честно оставляем прогресс или завершаем за ночь!
            // За ночь домашний дата-центр допарсит то, что уже было запущено.
            foreach (var p in ParseJobs)
            {
                if (!p.Completed)
                {
                    p.Elapsed = p.Duration;
                    p.Completed = true;
                    var srv = Servers.FirstOrDefault(s => s.Id == p.ServerId);
                    if (srv != null)
                    {
                        srv.IsParsing = false;
                        srv.IsParsed = true;
                    }
                }
            }

            int baseSalary = 100;
            int rentBills = 60;
            bool overslept = Fatigue >= 80;
            int oversleepPenalty = overslept ? 70 : 0;

            int endOfDayDelta = baseSalary - rentBills - oversleepPenalty;
            Money += endOfDayDelta;

            string headline;
            string newsBody;
            if (_decisionsToday.Contains(MoralBranch.Blog))
            {
                headline = "СРОЧНО: Акции L.A.R.P. Corp. упали на 14% после анонимного слива!";
                newsBody = "Загадочный блогер опубликовал внутренние документы корпорации. СБ начала внутреннее расследование.";
            }
            else if (_decisionsToday.Contains(MoralBranch.Dossier))
            {
                headline = "ИНСАЙД: Независимые журналисты готовят пакет улик против техногиганта";
                newsBody = "Мия подтвердила получение первых проверенных архивов. Власти запросили предварительную проверку.";
            }
            else if (_decisionsToday.Contains(MoralBranch.Boss))
            {
                headline = "КАРЬЕРА: Шеф СБ Маркус Стоун отметил бдительность младшего аналитика";
                newsBody = "Уязвимость в сервере оперативно закрыта. Подозрения СБ в ваш адрес ослабли.";
            }
            else
            {
                headline = "СПОКОЙНАЯ СМЕНА: Корпорация L.A.R.P. работает в штатном режиме";
                newsBody = "Вы пока не передали ни одного распарсенного архива. Ночь завтра даст новый шанс.";
            }

            var report = new DaySummaryReport
            {
                CompletedDay = Day,
                BaseSalary = baseSalary,
                DayEarnedExtra = ExtraEarnedToday,
                RentAndBills = rentBills,
                Overslept = overslept,
                OversleepPenalty = oversleepPenalty,
                NetDelta = ExtraEarnedToday + endOfDayDelta - SpentToday,
                ResultingMoney = Money,
                Authority = Authority,
                Followers = Followers,
                Dossier = Dossier,
                Trace = Trace,
                NewsHeadline = headline,
                NewsBody = newsBody
            };

            IsModalOpen = true;
            AudioFeedbackManager.Instance?.PlayNotification();

            if (Money <= -150)
            {
                TriggerGameOver("Долговая яма! Из-за штрафов за недосып и нехватки средств вы потеряли убежище.");
                return report;
            }

            OnDaySummaryReady?.Invoke(report);
            NotifyChanged();
            return report;
        }

        public void StartNextDay()
        {
            Day++;
            bool hadOversleep = Fatigue >= 80;
            Fatigue = hadOversleep ? 15 : 0;
            HacksCompletedToday = 0;
            ExtraEarnedToday = 0;
            SpentToday = 0;
            _decisionsToday.Clear();
            IsModalOpen = false;

            if (Day == 2 && !Mail.Any(m => m.Id == "mail_day2_marcus"))
            {
                Mail.Insert(0, new MailMessageData
                {
                    Id = "mail_day2_marcus",
                    SenderName = "Маркус Стоун",
                    SenderRole = "Шеф СБ L.A.R.P. Corp.",
                    Subject = "[ДЕНЬ 2 — ТИКЕТ #209] Проверка после ночной активности",
                    Body =
                        "Алекс, за ночь в сети зафиксированы аномалии. Подтверди журнал дежурства по тикету #209.\n" +
                        "Кстати, в интранете появились страницы совета директоров (SRV-DIR-03) — держись от них подальше.",
                    DayReceived = 2,
                    HasTicketAction = true,
                    TicketButtonLabel = "Подтвердить журнал #209 (+$65, +10 Авторитет)",
                    TicketRewardMoney = 65,
                    TicketRewardAuthority = 10,
                    TicketFatigueCost = 5,
                    TicketResultNote = "Тикет #209 закрыт.",
                    LinkedUrl = "intranet.larp.corp/board-draft"
                });
            }

            PostToast($"Начался День {Day}. Новые задачи и серверы доступны на компьютере!");
            PlayerState.Instance?.ResetClockForDay(Day);
            NotifyChanged();
        }

        public void TriggerGameOver(string reason)
        {
            IsGameOver = true;
            GameOverReason = reason;
            IsModalOpen = true;
            AudioFeedbackManager.Instance?.PlayErrorBuzzer();
            OnGameOverTriggered?.Invoke(reason);
            NotifyChanged();
        }

        public string GetCurrentObjectiveHint()
        {
            if (IsGameOver)
                return "ИГРА ОКОНЧЕНА — нажмите «Начать День 1 заново».";

            var firstTicket = Mail.FirstOrDefault(m => m.Id == "mail_marcus_ticket1");
            if (firstTicket != null && !firstTicket.TicketCompleted)
                return "1) Откройте «Почту»: закройте рабочий тикет #104 Маркуса и прочтите случайное письмо о «Проекте ОКО».";

            var srv1 = Servers.FirstOrDefault(s => s.Id == "srv_eye_01");
            if (srv1 != null && !srv1.IsHacked)
            {
                var frags = GetFragmentsForServer(srv1.Id);
                int found = frags.Count(f => f.IsFound);
                int decoded = frags.Count(f => f.IsFound && f.IsDecoded);
                if (found < 3)
                    return $"2) Откройте «Браузер»: найдите все 3 части ключа SRV-EYE-01 ({found}/3). Используйте «Исходный код» и Поиск!";
                if (decoded < 3)
                    return $"3) Откройте «Декодер»: расшифруйте найденные части ключа ({decoded}/3) и соберите ключ доступа.";
                return "4) Откройте «Bruteforce»: вставьте ключ SRV-EYE-01 и пройдите обход файрвола на время!";
            }

            if (GetActiveParseCount() > 0)
                return "5) Дата-Центр парсит сырые данные! Пока ждёте — выполните заказ в «Заказах и DarkNet» или улучшите Дата-Центр.";

            if (GetReadyEvidenceCount() > 0)
                return "6) Откройте «Дата-Центр»: компромат готов! Выберите путь: В Досье, В Блог или Сдать Боссу.";

            if (HacksCompletedToday == 1 && Fatigue < 95)
                return "7) 1-й взлом завершён! Рискните на 2-й взлом (SRV-FIN-02) ИЛИ встаньте (ESC) и лягте спать на диване в гостиной.";

            return "Вы отлично поработали! Встаньте из-за ПК (ESC) и кликните по дивану в комнате (или «Лечь спать»), чтобы подвести итоги дня.";
        }

        public void NotifyChanged()
        {
            OnStateChanged?.Invoke();
        }
    }
}
