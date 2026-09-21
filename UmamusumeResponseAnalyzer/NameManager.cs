using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using UmamusumeResponseAnalyzer.Entities;
using static UmamusumeResponseAnalyzer.Localization.NameManager;

namespace UmamusumeResponseAnalyzer
{
    public sealed class NameManager
    {
        private readonly FrozenDictionary<int, BaseName> names;

        public NameManager(IEnumerable<BaseName> data)
        {
            names = data.Select(Clone).ToFrozenDictionary(x => x.Id);
        }

        public string DisplayName(int id) => names.TryGetValue(id, out var value)
            ? value switch
            {
                SupportCardName supportCard => $"{supportCard.TypeName}{DisplayName(supportCard.CharaId)}",
                UmaName uma => DisplayName(uma.CharaId),
                _ => value.Name,
            }
            : $"{I18N_Unknown} ({id})";

        public string DisplayNickname(int id) => names.TryGetValue(id, out var value)
            ? value is SupportCardName supportCard
                ? $"{supportCard.TypeName}{supportCard.Nickname}"
                : value.Nickname
            : $"{I18N_Unknown} ({id})";

        public bool TryGetCharacter(int id, [NotNullWhen(true)] out BaseName? character)
        {
            if (names.TryGetValue(id, out var value)
                && value is not SupportCardName
                && value is not UmaName)
            {
                character = value;
                return true;
            }

            character = null;
            return false;
        }

        public BaseName GetRequiredCharacter(int id)
            => TryGetCharacter(id, out var character)
                ? character
                : throw Missing(id, nameof(BaseName));

        public bool TryGetSupportCard(int id, [NotNullWhen(true)] out SupportCardName? supportCard)
        {
            supportCard = names.GetValueOrDefault(id) as SupportCardName;
            return supportCard is not null;
        }

        public SupportCardName GetRequiredSupportCard(int id)
            => TryGetSupportCard(id, out var supportCard)
                ? supportCard
                : throw Missing(id, nameof(SupportCardName));

        public bool TryGetUmamusume(int id, [NotNullWhen(true)] out UmaName? umamusume)
        {
            umamusume = names.GetValueOrDefault(id) as UmaName;
            return umamusume is not null;
        }

        public UmaName GetRequiredUmamusume(int id)
            => TryGetUmamusume(id, out var umamusume)
                ? umamusume
                : throw Missing(id, nameof(UmaName));

        public int GetRequiredRSupportCardTypeByCharaId(int charaId)
        {
            var matches = names.Values
                .OfType<SupportCardName>()
                .Where(card => card.Id is >= 10000 and <= 12000 && card.CharaId == charaId)
                .Take(2)
                .ToArray();
            return matches.Length switch
            {
                1 => matches[0].Type,
                0 => throw new KeyNotFoundException(string.Format(I18N_RSupportCardMissing, charaId)),
                _ => throw new InvalidDataException(string.Format(I18N_RSupportCardAmbiguous, charaId))
            };
        }

        private static BaseName Clone(BaseName value) => value switch
        {
            SupportCardName card => new SupportCardName(card.Id, card.Name, card.Nickname, card.Type, card.CharaId),
            UmaName uma => new UmaName(uma.Id, uma.Name, uma.Nickname, uma.CharaId),
            _ => new BaseName(value.Id, value.Name, value.Nickname)
        };

        private static Exception Missing(int id, string expectedType)
            => new KeyNotFoundException(string.Format(I18N_NameMissing, id, expectedType));
    }
}
