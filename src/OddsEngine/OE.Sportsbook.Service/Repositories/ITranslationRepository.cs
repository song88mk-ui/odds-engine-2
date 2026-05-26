using OE.Shared.Models;

namespace OE.Sportsbook.Service.Repositories
{
    public interface ITranslationRepository
    {
        Dictionary<string, List<TranslationItem>> GetTranslationsAsync(List<string> stringIds);
    }
}
