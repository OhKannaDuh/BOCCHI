using BOCCHI.Common.Data.EventDrops;

namespace BOCCHI.Common.Services;

public interface IFieldNoteTracker
{
    bool HasNote(MonsterNote note);

    bool NeedsNote(MonsterNote note) => !HasNote(note);

    bool HasEntry(FieldNoteTargets.Entry entry);

    bool ShouldPursueFate(uint fateId);

    bool ShouldPursueCriticalEncounter(uint encounterId);
}
