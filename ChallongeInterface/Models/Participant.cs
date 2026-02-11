using Newtonsoft.Json;

namespace ChallongeInterface.Models;

public sealed class Participant
{
    [JsonProperty("id")]
    public long Id { get; init; }

    [JsonProperty("active")]
    public bool? Active { get; init; }

    [JsonProperty("checked_in_at")]
    public DateTimeOffset? CheckedInAt { get; init; }

    [JsonProperty("created_at")]
    public DateTimeOffset? CreatedAt { get; init; }

    [JsonProperty("name")]
    public string? Name { get; init; }

    [JsonProperty("seed")]
    public int? Seed { get; init; }

    [JsonProperty("username")]
    public string? Username { get; init; }

    [JsonProperty("challonge_username")]
    public string? ChallongeUsername { get; init; }

    [JsonProperty("challonge_email_address_verified")]
    public bool? ChallongeEmailAddressVerified { get; init; }

    [JsonProperty("email")]
    public string? Email { get; init; }

    [JsonProperty("email_hash")]
    public string? EmailHash { get; init; }

    [JsonProperty("final_rank")]
    public int? FinalRank { get; init; }

    [JsonProperty("group_id")]
    public long? GroupId { get; init; }

    [JsonProperty("icon")]
    public string? Icon { get; init; }

    [JsonProperty("invitation_id")]
    public long? InvitationId { get; init; }

    [JsonProperty("invite_email")]
    public string? InviteEmail { get; init; }

    [JsonProperty("misc")]
    public string? Misc { get; init; }

    [JsonProperty("on_waiting_list")]
    public bool? OnWaitingList { get; init; }

    [JsonProperty("state")]
    public string? State { get; init; }

    [JsonProperty("tournament_id")]
    public long? TournamentId { get; init; }

    [JsonProperty("updated_at")]
    public DateTimeOffset? UpdatedAt { get; init; }

    [JsonProperty("removable")]
    public bool? Removable { get; init; }

    [JsonProperty("participatable_or_invitation_attached")]
    public bool? ParticipatableOrInvitationAttached { get; init; }

    [JsonProperty("confirm_remove")]
    public bool? ConfirmRemove { get; init; }

    [JsonProperty("invitation_pending")]
    public bool? InvitationPending { get; init; }

    [JsonProperty("display_name_with_invitation_email_address")]
    public string? DisplayNameWithInvitationEmailAddress { get; init; }

    [JsonProperty("attached_participatable_portrait_url")]
    public string? AttachedParticipatablePortraitUrl { get; init; }

    [JsonProperty("can_check_in")]
    public bool? CanCheckIn { get; init; }

    [JsonProperty("checked_in")]
    public bool? CheckedIn { get; init; }

    [JsonProperty("reactivatable")]
    public bool? Reactivatable { get; init; }

    public string? DisplayName => Name ?? DisplayNameWithInvitationEmailAddress;
}
