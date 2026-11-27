using System;
using System.Collections.Generic;

namespace Project.LarpCore
{
    public enum MoralBranch
    {
        None,
        Dossier,
        Blog,
        Boss
    }

    public enum KeyEncoding
    {
        Plain,
        Base64,
        Hex,
        Rot13
    }

    public enum UpgradeType
    {
        DataCenterUpgrade,
        ClearLogsVpn,
        Printer3D,
        CoffeeEnergy
    }

    [Serializable]
    public class KeyFragmentData
    {
        public string Id;
        public string TargetServerId;
        public int SlotIndex; // 1..3
        public string RawValue;
        public string DecodedValue;
        public KeyEncoding Encoding;
        public string SourceUrl;
        public string Hint;
        public bool IsFound;
        public bool IsDecoded;

        public bool NeedsDecoding => Encoding != KeyEncoding.Plain;
    }

    [Serializable]
    public class ServicePageData
    {
        public string Url;
        public string Title;
        public string Subtitle;
        public string VisibleBody;
        public bool HasInteractiveHiddenBlock;
        public string InteractiveBlockLabel;
        public string InteractiveBlockRevealText;
        public string HtmlSource;
        public string FragmentId;
        public int UnlockDay = 1;
        public bool IsUnlocked = true;
        public bool InteractiveRevealed;
    }

    [Serializable]
    public class HackServerData
    {
        public string Id;
        public string DisplayName;
        public string IpAddress;
        public string Description;
        public string RequiredKey;
        public int Difficulty; // 1..3 (columns/speed in bypass minigame)
        public int FatigueCost;
        public float BaseParseSeconds;
        public int UnlockDay = 1;
        public string EvidenceTitle;
        public string EvidenceSummary;

        public bool IsHacked;
        public bool IsParsing;
        public bool IsParsed;
        public bool IsDecided;
        public MoralBranch ChosenBranch = MoralBranch.None;
    }

    [Serializable]
    public class ActiveParseJob
    {
        public string ServerId;
        public string ServerName;
        public float Elapsed;
        public float Duration;
        public bool Completed;

        public float Progress => Duration <= 0.01f ? 1f : Math.Clamp(Elapsed / Duration, 0f, 1f);
    }

    [Serializable]
    public class MailMessageData
    {
        public string Id;
        public string SenderName;
        public string SenderRole;
        public string Subject;
        public string Body;
        public int DayReceived = 1;
        public bool IsRead;

        public bool HasTicketAction;
        public string TicketButtonLabel;
        public int TicketRewardMoney;
        public int TicketRewardAuthority;
        public int TicketFatigueCost;
        public bool TicketCompleted;
        public string TicketResultNote;

        public string LinkedUrl;
    }

    [Serializable]
    public class FreelanceJobData
    {
        public string Id;
        public string Title;
        public string Client;
        public string Description;
        public int RewardMoney;
        public int FatigueCost;
        public bool RequiresPrinter3D;
        public int UnlockDay = 1;
        public bool IsCompleted;
    }

    [Serializable]
    public class ShopItemData
    {
        public string Id;
        public string Title;
        public string Description;
        public int Cost;
        public UpgradeType Type;
        public bool Repeatable;
        public bool Purchased;
    }

    [Serializable]
    public class DaySummaryReport
    {
        public int CompletedDay;
        public int BaseSalary;
        public int DayEarnedExtra;
        public int RentAndBills;
        public bool Overslept;
        public int OversleepPenalty;
        public int NetDelta;
        public int ResultingMoney;
        public int Authority;
        public int Followers;
        public int Dossier;
        public int Trace;
        public string NewsHeadline;
        public string NewsBody;
    }

    public static class KeyDecoderUtil
    {
        public static string Decode(string input, KeyEncoding encoding)
        {
            if (string.IsNullOrWhiteSpace(input))
                return string.Empty;

            string trimmed = input.Trim();
            try
            {
                switch (encoding)
                {
                    case KeyEncoding.Plain:
                        return trimmed;

                    case KeyEncoding.Base64:
                        byte[] b64Bytes = Convert.FromBase64String(trimmed);
                        return System.Text.Encoding.UTF8.GetString(b64Bytes);

                    case KeyEncoding.Hex:
                        string cleanHex = trimmed.Replace(" ", "").Replace("0x", "").Replace("0X", "");
                        if (cleanHex.Length % 2 != 0) return "[ОШИБКА HEX]";
                        byte[] hexBytes = new byte[cleanHex.Length / 2];
                        for (int i = 0; i < hexBytes.Length; i++)
                        {
                            hexBytes[i] = Convert.ToByte(cleanHex.Substring(i * 2, 2), 16);
                        }
                        return System.Text.Encoding.UTF8.GetString(hexBytes);

                    case KeyEncoding.Rot13:
                        char[] chars = trimmed.ToCharArray();
                        for (int i = 0; i < chars.Length; i++)
                        {
                            char c = chars[i];
                            if (c >= 'A' && c <= 'Z')
                                chars[i] = (char)('A' + (c - 'A' + 13) % 26);
                            else if (c >= 'a' && c <= 'z')
                                chars[i] = (char)('a' + (c - 'a' + 13) % 26);
                        }
                        return new string(chars);

                    default:
                        return trimmed;
                }
            }
            catch
            {
                return "[НЕВЕРНЫЙ ФОРМАТ]";
            }
        }

        public static string GetEncodingLabel(KeyEncoding encoding)
        {
            switch (encoding)
            {
                case KeyEncoding.Plain: return "PLAIN (Открытый)";
                case KeyEncoding.Base64: return "BASE64";
                case KeyEncoding.Hex: return "HEX (16-ричный)";
                case KeyEncoding.Rot13: return "ROT13 (Цезарь)";
                default: return encoding.ToString();
            }
        }
    }
}
