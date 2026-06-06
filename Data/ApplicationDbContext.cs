using InfinityAI.Api.Models.Database;
using InfinityAI.Api.Models.Database.Identity;
using InfinityAI.Api.Models.Llm;
using Microsoft.EntityFrameworkCore;

namespace InfinityAI.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

    public DbSet<Document>                  Documents                  => Set<Document>();
    public DbSet<DocumentChunk>             DocumentChunks             => Set<DocumentChunk>();
    public DbSet<DocumentChunkEmbedding>    DocumentChunkEmbeddings    => Set<DocumentChunkEmbedding>();
    public DbSet<ApplicationFile>           Files                      => Set<ApplicationFile>();
    public DbSet<StoredFile>                StoredFiles                => Set<StoredFile>();
    public DbSet<ApplicationUser>           Users                      => Set<ApplicationUser>();
    public DbSet<AiSettings>                AiSettings                 => Set<AiSettings>();
    public DbSet<AiProfile>                 AiProfiles                 => Set<AiProfile>();
    public DbSet<AiProfileProvider>         AiProfileProviders         => Set<AiProfileProvider>();
    public DbSet<LlmModel>                  LlmModels                  => Set<LlmModel>();
    public DbSet<LlmProvider>               LlmProviders               => Set<LlmProvider>();
    public DbSet<LlmModelCapabilityMetric>  LlmModelCapabilityMetrics  => Set<LlmModelCapabilityMetric>();
    public DbSet<AiRequestLog>              AiRequestLogs              => Set<AiRequestLog>();
    public DbSet<PipelineComponentSetting>  PipelineComponentSettings  => Set<PipelineComponentSetting>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.EntraObjectId).HasMaxLength(128).IsRequired();
            e.Property(x => x.DisplayName).HasMaxLength(255);
            e.Property(x => x.Email).HasMaxLength(191);
        });

        builder.Entity<ApplicationFile>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.OriginalFileName).HasMaxLength(512).IsRequired();
            e.Property(x => x.StoredFileName).HasMaxLength(512).IsRequired();
            e.Property(x => x.ContentType).HasMaxLength(255).IsRequired();
            e.Property(x => x.FileExtension).HasMaxLength(32).IsRequired();
            e.Property(x => x.FileType).HasMaxLength(64).IsRequired();
            e.Property(x => x.StoragePath).HasMaxLength(1024).IsRequired();
            e.Property(x => x.ContentHash).HasMaxLength(64);
            e.Property(x => x.Source).HasMaxLength(64).IsRequired();
        });

        builder.Entity<StoredFile>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Sha256Hash).HasMaxLength(64).IsRequired();
            e.Property(x => x.StoragePath).HasMaxLength(1024).IsRequired();
            e.Property(x => x.ContentType).HasMaxLength(255).IsRequired();
            e.Property(x => x.FileExtension).HasMaxLength(32).IsRequired();
            e.ToTable("StoredFiles");
        });

        builder.Entity<DocumentChunk>(e =>
        {
            e.Property(x => x.Heading).HasMaxLength(500);
        });

        builder.Entity<DocumentChunkEmbedding>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.EmbeddingProvider).HasMaxLength(128).HasDefaultValue("").IsRequired();
            e.Property(x => x.Model).HasMaxLength(128).IsRequired();
            e.Property(x => x.VectorJson).HasColumnType("longtext").IsRequired();
            e.HasOne(x => x.DocumentChunk)
                .WithMany(x => x.Embeddings)
                .HasForeignKey(x => x.DocumentChunkId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<LlmProvider>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.ProviderType).HasMaxLength(50).IsRequired();
            e.HasMany(x => x.Models)
                .WithOne(x => x.LlmProvider)
                .HasForeignKey(x => x.LlmProviderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<LlmModel>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.ModelId).HasMaxLength(150).IsRequired();
            e.Property(x => x.DisplayName).HasMaxLength(150).IsRequired();
            e.Property(x => x.InputCostPer1M).HasPrecision(18, 8);
            e.Property(x => x.OutputCostPer1M).HasPrecision(18, 8);
            e.Property(x => x.EmbeddingCostPer1M).HasPrecision(18, 8);
            e.Property(x => x.ImageCostPerGeneration).HasPrecision(18, 8);
        });

        builder.Entity<AiProfile>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
        });

        builder.Entity<AiProfileProvider>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne(x => x.AiProfile)
                .WithMany(x => x.AllowedProviders)
                .HasForeignKey(x => x.AiProfileId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.LlmProvider)
                .WithMany()
                .HasForeignKey(x => x.LlmProviderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<LlmModelCapabilityMetric>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne(x => x.LlmModel)
                .WithMany()
                .HasForeignKey(x => x.LlmModelId)
                .OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Capability).HasConversion<int>();
        });

        builder.Entity<AiSettings>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne(x => x.ActiveAiProfile)
                .WithMany()
                .HasForeignKey(x => x.ActiveAiProfileId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<AiRequestLog>(e =>
        {
            e.Property(x => x.Capability).HasConversion<string>().HasMaxLength(64);
            e.Property(x => x.FailureType).HasMaxLength(64);
            e.Property(x => x.RequestId).HasMaxLength(64);
            e.Property(x => x.ProviderErrorCode).HasMaxLength(128);
            e.Property(x => x.ProviderErrorMessage).HasMaxLength(1000);
        });
    }
}
