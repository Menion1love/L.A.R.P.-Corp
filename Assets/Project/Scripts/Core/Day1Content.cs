using System.Collections.Generic;

namespace Project.LarpCore
{
    public static class Day1Content
    {
        public static List<HackServerData> CreateServers()
        {
            return new List<HackServerData>
            {
                new HackServerData
                {
                    Id = "srv_eye_01",
                    DisplayName = "SRV-EYE-01 — Архив «Проект ОКО»",
                    IpAddress = "10.42.8.14",
                    Description = "Закрытый сервер отдела мониторинга L.A.R.P. Corp. Случайно упомянут в служебной рассылке.",
                    RequiredKey = "LARP-7419-ROOT",
                    Difficulty = 1,
                    FatigueCost = 45,
                    BaseParseSeconds = 12f,
                    UnlockDay = 1,
                    EvidenceTitle = "Архив #01: Массовая слежка «Проект ОКО»",
                    EvidenceSummary = "Логи подтверждают: руководство L.A.R.P. Corp. нелегально собирает данные с домашних роутеров 400 000 граждан и продаёт профили третьим лицам."
                },
                new HackServerData
                {
                    Id = "srv_fin_02",
                    DisplayName = "SRV-FIN-02 — Чёрная касса СБ",
                    IpAddress = "10.42.19.88",
                    Description = "Дополнительный сервер финансового аудита. Повышенный уровень защиты — идеален для рискованного 2-го взлома за ночь.",
                    RequiredKey = "CORP-9021-SYNC",
                    Difficulty = 2,
                    FatigueCost = 45,
                    BaseParseSeconds = 16f,
                    UnlockDay = 1,
                    EvidenceTitle = "Архив #02: Подкуп инспекторов и откаты СБ",
                    EvidenceSummary = "Платёжные ведомости с подписями топ-менеджмента: $2.4M переведено на офшоры для закрытия государственной проверки."
                },
                new HackServerData
                {
                    Id = "srv_dir_03",
                    DisplayName = "SRV-DIR-03 — Совет директоров: «Омега»",
                    IpAddress = "10.42.99.201",
                    Description = "Защищённый узел руководства (доступен со 2-го дня). Требует высокой концентрации при обходе файрвола.",
                    RequiredKey = "OMEGA-512-NODE",
                    Difficulty = 3,
                    FatigueCost = 50,
                    BaseParseSeconds = 20f,
                    UnlockDay = 2,
                    EvidenceTitle = "Архив #03: Протокол «Омега» — Ликвидация свидетелей",
                    EvidenceSummary = "Внутреннее распоряжение совета директоров о блокировке счетов и фабрикации дел против бывших аналитиков компании."
                }
            };
        }

        public static List<KeyFragmentData> CreateKeyFragments()
        {
            return new List<KeyFragmentData>
            {
                // Сервер 1: LARP-7419-ROOT
                new KeyFragmentData
                {
                    Id = "frag_eye_1",
                    TargetServerId = "srv_eye_01",
                    SlotIndex = 1,
                    RawValue = "LARP",
                    DecodedValue = "LARP",
                    Encoding = KeyEncoding.Plain,
                    SourceUrl = "intranet.larp.corp/eye-monitor",
                    Hint = "Скрытый интерактивный блок на странице мониторинга.",
                    IsDecoded = true
                },
                new KeyFragmentData
                {
                    Id = "frag_eye_2",
                    TargetServerId = "srv_eye_01",
                    SlotIndex = 2,
                    RawValue = "NzQxOQ==",
                    DecodedValue = "7419",
                    Encoding = KeyEncoding.Base64,
                    SourceUrl = "intranet.larp.corp/dev-notes",
                    Hint = "HTML-комментарий в исходном коде (формат Base64, заканчивается на '==').",
                    IsDecoded = false
                },
                new KeyFragmentData
                {
                    Id = "frag_eye_3",
                    TargetServerId = "srv_eye_01",
                    SlotIndex = 3,
                    RawValue = "524F4F54",
                    DecodedValue = "ROOT",
                    Encoding = KeyEncoding.Hex,
                    SourceUrl = "intranet.larp.corp/server-status",
                    Hint = "Атрибут в исходном коде статуса шлюза (формат HEX).",
                    IsDecoded = false
                },

                // Сервер 2: CORP-9021-SYNC
                new KeyFragmentData
                {
                    Id = "frag_fin_1",
                    TargetServerId = "srv_fin_02",
                    SlotIndex = 1,
                    RawValue = "CORP",
                    DecodedValue = "CORP",
                    Encoding = KeyEncoding.Plain,
                    SourceUrl = "intranet.larp.corp/fin-audit",
                    Hint = "Скрытая заметка бухгалтера на странице отчёта.",
                    IsDecoded = true
                },
                new KeyFragmentData
                {
                    Id = "frag_fin_2",
                    TargetServerId = "srv_fin_02",
                    SlotIndex = 2,
                    RawValue = "OTAyMQ==",
                    DecodedValue = "9021",
                    Encoding = KeyEncoding.Base64,
                    SourceUrl = "intranet.larp.corp/hr-backup",
                    Hint = "Закодировано в Base64 внутри исходного кода бэкапа.",
                    IsDecoded = false
                },
                new KeyFragmentData
                {
                    Id = "frag_fin_3",
                    TargetServerId = "srv_fin_02",
                    SlotIndex = 3,
                    RawValue = "FLAP",
                    DecodedValue = "SYNC",
                    Encoding = KeyEncoding.Rot13,
                    SourceUrl = "intranet.larp.corp/sec-gate",
                    Hint = "Сдвиг Цезаря ROT13 в комментарии к правилам файрвола.",
                    IsDecoded = false
                },

                // Сервер 3 (День 2): OMEGA-512-NODE
                new KeyFragmentData
                {
                    Id = "frag_dir_1",
                    TargetServerId = "srv_dir_03",
                    SlotIndex = 1,
                    RawValue = "BZRTN",
                    DecodedValue = "OMEGA",
                    Encoding = KeyEncoding.Rot13,
                    SourceUrl = "intranet.larp.corp/board-draft",
                    Hint = "ROT13-строка в скрытом блоке повестки совета.",
                    IsDecoded = false
                },
                new KeyFragmentData
                {
                    Id = "frag_dir_2",
                    TargetServerId = "srv_dir_03",
                    SlotIndex = 2,
                    RawValue = "NTEy",
                    DecodedValue = "512",
                    Encoding = KeyEncoding.Base64,
                    SourceUrl = "intranet.larp.corp/omega-gate",
                    Hint = "Base64 в исходном коде шлюза «Омега».",
                    IsDecoded = false
                },
                new KeyFragmentData
                {
                    Id = "frag_dir_3",
                    TargetServerId = "srv_dir_03",
                    SlotIndex = 3,
                    RawValue = "4E4F4445",
                    DecodedValue = "NODE",
                    Encoding = KeyEncoding.Hex,
                    SourceUrl = "intranet.larp.corp/omega-log",
                    Hint = "HEX-идентификатор в исходном коде журнала.",
                    IsDecoded = false
                }
            };
        }

        public static List<ServicePageData> CreateServicePages()
        {
            return new List<ServicePageData>
            {
                // --- Страницы Сервера #1 (День 1) ---
                new ServicePageData
                {
                    Url = "intranet.larp.corp/eye-monitor",
                    Title = "Портал мониторинга L.A.R.P. [SRV-EYE-01]",
                    Subtitle = "Внутренняя служебная страница — только для сотрудников отдела аналитики",
                    VisibleBody =
                        "Статус узла «Проект ОКО»: АКТИВЕН (сбор телеметрии 24/7).\n" +
                        "Внимание дежурным: не оставляйте токены доступа на видном месте перед плановым аудитом СБ.\n\n" +
                        "Последняя синхронизация узлов прошла в 19:40. Ниже расположен свёрнутый сервисный блок диагностики.",
                    HasInteractiveHiddenBlock = true,
                    InteractiveBlockLabel = "[!] Раскрыть скрытый сервисный блок #1",
                    InteractiveBlockRevealText = "DIAG_DUMP :: KEY_PART_1 (SRV-EYE-01) = LARP  [формат: PLAIN]",
                    HtmlSource =
                        "<!DOCTYPE html>\n<html>\n<head><title>L.A.R.P. Eye Monitor</title></head>\n<body>\n" +
                        "  <h1>Портал мониторинга ОКО</h1>\n" +
                        "  <div class=\"status\">NODE: 10.42.8.14 [ONLINE]</div>\n" +
                        "  <div class=\"hidden-debug\" style=\"color:#111\">\n" +
                        "    <!-- KEY_PART_1: LARP (Plain) -->\n" +
                        "  </div>\n</body>\n</html>",
                    FragmentId = "frag_eye_1",
                    UnlockDay = 1
                },
                new ServicePageData
                {
                    Url = "intranet.larp.corp/dev-notes",
                    Title = "Заметки системного администратора [v0.9]",
                    Subtitle = "Черновик миграции базы данных — забыли закрыть от индексации",
                    VisibleBody =
                        "Чек-лист на эту неделю:\n" +
                        "1. Перенести бэкапы камер и роутеров на новый кластер.\n" +
                        "2. Убрать временный фрагмент ключа #2 из HTML-комментариев до проверки Маркуса Стоуна.\n" +
                        "3. Подсказка для себя: вторую часть ключа я завернул в Base64, чтобы сканер текста не ругался.\n\n" +
                        "Подсказка: нажмите «Исходный код» или воспользуйтесь Поиском по слову KEY.",
                    HasInteractiveHiddenBlock = false,
                    HtmlSource =
                        "<!DOCTYPE html>\n<html>\n<body>\n" +
                        "  <h2>Admin Migration Notes</h2>\n" +
                        "  <p>Backup schedule: 03:00 UTC</p>\n" +
                        "  <!-- TODO: DELETE BEFORE AUDIT! -->\n" +
                        "  <!-- KEY_PART_2 (SRV-EYE-01) [BASE64]: NzQxOQ== -->\n" +
                        "  <footer data-owner=\"sysadmin_k\">L.A.R.P. Intranet</footer>\n" +
                        "</body>\n</html>",
                    FragmentId = "frag_eye_2",
                    UnlockDay = 1
                },
                new ServicePageData
                {
                    Url = "intranet.larp.corp/server-status",
                    Title = "Шлюз безопасности — Телеметрия портов",
                    Subtitle = "Отчёт автоматической проверки 10.42.8.14",
                    VisibleBody =
                        "Порты шлюза SRV-EYE-01:\n" +
                        "• TCP/443  — HTTPS API [OK]\n" +
                        "• TCP/8443 — Внутренний брутфорс-контроль [ARMED]\n" +
                        "• Trace-система: реагирует на ошибки обхода и задержки.\n\n" +
                        "Технический хвостовой токен (часть #3) скрыт в метаданных исходного кода страницы в 16-ричном (HEX) виде.",
                    HasInteractiveHiddenBlock = false,
                    HtmlSource =
                        "<!DOCTYPE html>\n<html>\n<head>\n" +
                        "  <meta name=\"gateway\" content=\"SRV-EYE-01\" />\n" +
                        "  <meta name=\"KEY_PART_3_HEX\" content=\"524F4F54\" />\n" +
                        "</head>\n<body>\n" +
                        "  <pre>ALL SERVICES NOMINAL. FIREWALL ACTIVE.</pre>\n" +
                        "</body>\n</html>",
                    FragmentId = "frag_eye_3",
                    UnlockDay = 1
                },

                // --- Страницы Сервера #2 (День 1 — 2-й взлом) ---
                new ServicePageData
                {
                    Url = "intranet.larp.corp/fin-audit",
                    Title = "Финансовый шлюз СБ [SRV-FIN-02]",
                    Subtitle = "Сводка внебюджетных расходов службы безопасности",
                    VisibleBody =
                        "Квартальный отчёт по статье «Консалтинг и аудит».\n" +
                        "Суммы скрыты на уровне интерфейса, однако внизу страницы оставлена свёрнутая строка доступа бухгалтера.",
                    HasInteractiveHiddenBlock = true,
                    InteractiveBlockLabel = "[!] Показать скрытую сноску бухгалтера",
                    InteractiveBlockRevealText = "FIN_PREFIX :: KEY_PART_1 (SRV-FIN-02) = CORP  [формат: PLAIN]",
                    HtmlSource =
                        "<html><body>\n" +
                        "  <h1>SB Finance Gateway</h1>\n" +
                        "  <!-- KEY_PART_1 (SRV-FIN-02): CORP -->\n" +
                        "</body></html>",
                    FragmentId = "frag_fin_1",
                    UnlockDay = 1
                },
                new ServicePageData
                {
                    Url = "intranet.larp.corp/hr-backup",
                    Title = "Резервный индекс отдела кадров",
                    Subtitle = "Автогенерируемый дамп прав доступа",
                    VisibleBody =
                        "Список временных пропусков в финансовый контур обновлён.\n" +
                        "Для поиска зашифрованного фрагмента #2 откройте исходный код страницы.",
                    HasInteractiveHiddenBlock = false,
                    HtmlSource =
                        "<html><body>\n" +
                        "  <ul><li>user: m_stone [ADMIN]</li><li>user: a_nullptr [JUNIOR]</li></ul>\n" +
                        "  <!-- KEY_PART_2 (SRV-FIN-02) [BASE64]: OTAyMQ== -->\n" +
                        "</body></html>",
                    FragmentId = "frag_fin_2",
                    UnlockDay = 1
                },
                new ServicePageData
                {
                    Url = "intranet.larp.corp/sec-gate",
                    Title = "Конфигурация прокси СБ",
                    Subtitle = "Правила маршрутизации 10.42.19.88",
                    VisibleBody =
                        "Внутренний прокси использует простую обфускацию ROT13 для сервисных тегов.\n" +
                        "Проверьте исходный код, чтобы извлечь 3-ю часть ключа SRV-FIN-02.",
                    HasInteractiveHiddenBlock = false,
                    HtmlSource =
                        "<html><body>\n" +
                        "  <code>proxy_pass https://10.42.19.88;</code>\n" +
                        "  <!-- KEY_PART_3 (SRV-FIN-02) [ROT13]: FLAP -->\n" +
                        "</body></html>",
                    FragmentId = "frag_fin_3",
                    UnlockDay = 1
                },

                // --- Страницы Сервера #3 (День 2) ---
                new ServicePageData
                {
                    Url = "intranet.larp.corp/board-draft",
                    Title = "Повестка совета директоров [SRV-DIR-03]",
                    Subtitle = "Доступ открыт после событий 1-го дня",
                    VisibleBody =
                        "Закрытое совещание по проекту «Омега». Часть заголовка зашифрована сдвигом ROT13.",
                    HasInteractiveHiddenBlock = true,
                    InteractiveBlockLabel = "[!] Раскрыть черновик повестки",
                    InteractiveBlockRevealText = "KEY_PART_1 (SRV-DIR-03) [ROT13] = BZRTN",
                    HtmlSource = "<html><body><!-- KEY_PART_1 [ROT13]: BZRTN --></body></html>",
                    FragmentId = "frag_dir_1",
                    UnlockDay = 2
                },
                new ServicePageData
                {
                    Url = "intranet.larp.corp/omega-gate",
                    Title = "Шлюз Омега — Авторизация",
                    Subtitle = "Секретный контур 10.42.99.201",
                    VisibleBody = "Проверьте исходный код шлюза для извлечения Base64-фрагмента #2.",
                    HasInteractiveHiddenBlock = false,
                    HtmlSource = "<html><body><!-- KEY_PART_2 (SRV-DIR-03) [BASE64]: NTEy --></body></html>",
                    FragmentId = "frag_dir_2",
                    UnlockDay = 2
                },
                new ServicePageData
                {
                    Url = "intranet.larp.corp/omega-log",
                    Title = "Журнал узла Омега",
                    Subtitle = "Технический дамп HEX-идентификаторов",
                    VisibleBody = "Найдите HEX-метку KEY_PART_3 в исходном коде страницы.",
                    HasInteractiveHiddenBlock = false,
                    HtmlSource = "<html><body><!-- KEY_PART_3 (SRV-DIR-03) [HEX]: 4E4F4445 --></body></html>",
                    FragmentId = "frag_dir_3",
                    UnlockDay = 2
                }
            };
        }

        public static List<MailMessageData> CreateDay1Mail()
        {
            return new List<MailMessageData>
            {
                new MailMessageData
                {
                    Id = "mail_marcus_ticket1",
                    SenderName = "Маркус Стоун",
                    SenderRole = "Шеф СБ L.A.R.P. Corp.",
                    Subject = "[ТИКЕТ #104] Срочная сверка логов смены",
                    Body =
                        "Алекс, задержись вечером на 5 минут — нужно закрыть тикет #104 до утренней планёрки.\n" +
                        "В отделе аналитики висит неподтверждённый отчёт по доступам.\n\n" +
                        "Закрой тикет кнопкой ниже — начислю премию $50 и отмечу тебя в табеле (Авторитет +10).",
                    DayReceived = 1,
                    HasTicketAction = true,
                    TicketButtonLabel = "Выполнить тикет #104 (+$50, +10 Авторитет)",
                    TicketRewardMoney = 50,
                    TicketRewardAuthority = 10,
                    TicketFatigueCost = 5,
                    TicketResultNote = "Тикет #104 закрыт. Маркус доволен вашей исполнительностью."
                },
                new MailMessageData
                {
                    Id = "mail_leak_accident",
                    SenderName = "ИТ-Рассылка [ОШИБКА АДРЕСАЦИИ]",
                    SenderRole = "Внутренний бот L.A.R.P.",
                    Subject = "RE: Проект «ОКО» — срочно почистить служебные страницы!",
                    Body =
                        "[СООБЩЕНИЕ ОТПРАВЛЕНО ВАМ ПО ОШИБКЕ]\n\n" +
                        "Коллеги, на сервере SRV-EYE-01 (10.42.8.14) лежат полные архивы слежки за клиентами.\n" +
                        "Кто-то из стажёров оставил части ключа на служебных страницах:\n" +
                        "• intranet.larp.corp/eye-monitor\n" +
                        "• intranet.larp.corp/dev-notes\n" +
                        "• intranet.larp.corp/server-status\n\n" +
                        "Срочно уберите их до аудита! — (Откройте вкладку «Браузер», чтобы осмотреть страницы).",
                    DayReceived = 1,
                    LinkedUrl = "intranet.larp.corp/eye-monitor"
                },
                new MailMessageData
                {
                    Id = "mail_mia_intro",
                    SenderName = "Мия",
                    SenderRole = "Независимый журналист (Ветка: Досье)",
                    Subject = "Если ты внутри L.A.R.P. — мне нужны доказательства",
                    Body =
                        "Привет, NullPtr. Я веду расследование против руководства L.A.R.P. Corp.\n" +
                        "Если сможешь вытащить и распарсить секретные архивы через свой домашний дата-центр — отправляй их в «Досье».\n" +
                        "Соберём дело и доведём верхушку корпорации до суда.",
                    DayReceived = 1
                },
                new MailMessageData
                {
                    Id = "mail_ghost_intro",
                    SenderName = "«Призрак»",
                    SenderRole = "Брокер DarkNet (Ветка: Фолловеры / Магазин)",
                    Subject = "Анонимный блог и апгрейды железа",
                    Body =
                        "Йоу. Видел, что ты поднял домашний дата-центр.\n" +
                        "Если сольёшь компромат в мой анонимный Блог — получишь подписчиков и живые деньги с донатов, а акции L.A.R.P. рухнут.\n" +
                        "Заглядывай в раздел «Заказы и DarkNet»: там есть апгрейд дата-центра, 3D-принтер, чистка логов (сброс Trace) и кофе.",
                    DayReceived = 1
                }
            };
        }

        public static List<FreelanceJobData> CreateFreelanceJobs()
        {
            return new List<FreelanceJobData>
            {
                new FreelanceJobData
                {
                    Id = "job_script_01",
                    Title = "Оптимизация SQL-запроса",
                    Client = "Фриланс-биржа",
                    Description = "Быстрая подработка: починить медленный индекс в таблице клиента.",
                    RewardMoney = 65,
                    FatigueCost = 15,
                    RequiresPrinter3D = false,
                    UnlockDay = 1
                },
                new FreelanceJobData
                {
                    Id = "job_parse_02",
                    Title = "Скрипт парсинга прайс-листов",
                    Client = "Магазин «ТехноСток»",
                    Description = "Написать простой HTML-парсер для выгрузки цен конкурентов.",
                    RewardMoney = 85,
                    FatigueCost = 20,
                    RequiresPrinter3D = false,
                    UnlockDay = 1
                },
                new FreelanceJobData
                {
                    Id = "job_printer_01",
                    Title = "3D-печать корпуса для Wi-Fi адаптера",
                    Client = "Контакт Призрака",
                    Description = "Изготовить на домашнем 3D-принтере скрытый корпус для сетевого сниффера и отправить курьером.",
                    RewardMoney = 150,
                    FatigueCost = 15,
                    RequiresPrinter3D = true,
                    UnlockDay = 1
                },
                new FreelanceJobData
                {
                    Id = "job_day2_01",
                    Title = "Аудит корпоративного VPN",
                    Client = "Стартап NovaNet",
                    Description = "Проверить конфигурацию туннеля и закрыть открытый порт.",
                    RewardMoney = 110,
                    FatigueCost = 20,
                    RequiresPrinter3D = false,
                    UnlockDay = 2
                }
            };
        }

        public static List<ShopItemData> CreateShopItems()
        {
            return new List<ShopItemData>
            {
                new ShopItemData
                {
                    Id = "upg_dc_2",
                    Title = "Апгрейд Дата-Центра → Ур. 2",
                    Description = "Добавляет серверные стойки. Ускоряет парсинг сырых архивов в 2 раза!",
                    Cost = 120,
                    Type = UpgradeType.DataCenterUpgrade,
                    Repeatable = false
                },
                new ShopItemData
                {
                    Id = "upg_dc_3",
                    Title = "Апгрейд Дата-Центра → Ур. 3",
                    Description = "Нейро-ускоритель парсинга. Архивы расшифровываются почти мгновенно (x4)!",
                    Cost = 240,
                    Type = UpgradeType.DataCenterUpgrade,
                    Repeatable = false
                },
                new ShopItemData
                {
                    Id = "upg_printer",
                    Title = "Купить 3D-Принтер для убежища",
                    Description = "Открывает высокооплачиваемые заказы на изготовление физических устройств.",
                    Cost = 180,
                    Type = UpgradeType.Printer3D,
                    Repeatable = false
                },
                new ShopItemData
                {
                    Id = "srv_vpn_clear",
                    Title = "Смена IP и чистка логов (-25% Trace)",
                    Description = "Прогоняет следы через цепочку прокси и сбивает подозрения СБ.",
                    Cost = 75,
                    Type = UpgradeType.ClearLogsVpn,
                    Repeatable = true
                },
                new ShopItemData
                {
                    Id = "item_coffee",
                    Title = "Двойной эспрессо (-25% Усталости)",
                    Description = "Бодрит на ночную смену, позволяя рискнуть и пойти на второй взлом.",
                    Cost = 35,
                    Type = UpgradeType.CoffeeEnergy,
                    Repeatable = true
                }
            };
        }
    }
}
