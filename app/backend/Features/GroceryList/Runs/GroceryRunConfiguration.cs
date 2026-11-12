using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LoginApp.Api.Features.GroceryList.Runs;

/// <summary>
/// EF Core mapping for <see cref="GroceryRun"/> and <see cref="GroceryRunParticipant"/>.
/// </summary>
internal sealed class GroceryRunConfiguration : IEntityTypeConfiguration<GroceryRun>, IEntityTypeConfiguration<GroceryRunParticipant>
{
    // Long enough for every status and outcome name.
    private const int EnumMaxLength = 20;

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<GroceryRun> builder)
    {
        // The app chooses the ID when it starts the workflow, and the workflow's activities create the row with it.
        builder.Property(run => run.Id).ValueGeneratedNever();
        builder.Property(run => run.Sequence).IsConcurrencyToken();

        // Stored by name, so the table reads plainly in psql.
        builder.Property(run => run.Status).HasConversion<string>().HasMaxLength(EnumMaxLength);
        builder.Property(run => run.LastCallOutcome).HasConversion<string>().HasMaxLength(EnumMaxLength);

        // Finding the current run looks for runs under way; there is rarely more than one, and never many.
        builder.HasIndex(run => run.Status);

        // A run outlives its shopper's account (the history stays); it just loses the name.
        builder.HasOne(run => run.Shopper)
            .WithMany()
            .HasForeignKey(run => run.ShopperId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(run => run.Participants)
            .WithOne()
            .HasForeignKey(participant => participant.RunId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<GroceryRunParticipant> builder)
    {
        builder.HasKey(participant => new { participant.RunId, participant.UserId });

        // A deleted account leaves the runs it was asked in; nobody waits for it any more.
        builder.HasOne(participant => participant.User)
            .WithMany()
            .HasForeignKey(participant => participant.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
