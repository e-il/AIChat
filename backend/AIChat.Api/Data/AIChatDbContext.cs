using Microsoft.EntityFrameworkCore;
using AIChat.Api.Models;

namespace AIChat.Api.Data;

public class AIChatDbContext : DbContext
{
    public AIChatDbContext(DbContextOptions<AIChatDbContext> options) : base(options)
    {
    }

    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<ChatMessage> Messages => Set<ChatMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Conversation>(entity =>
        {
            entity.HasKey(c => c.Id);
            entity.Property(c => c.Title).HasMaxLength(200);
            entity.HasMany(c => c.Messages)
                  .WithOne()
                  .HasForeignKey(m => m.ConversationId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ChatMessage>(entity =>
        {
            entity.HasKey(m => m.Id);
            entity.Property(m => m.Role).HasMaxLength(20);
            entity.HasIndex(m => m.ConversationId);
        });
    }
}
