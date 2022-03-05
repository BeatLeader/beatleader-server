﻿// <auto-generated />
using System;
using BeatLeader_Server;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

#nullable disable

namespace BeatLeader_Server.Migrations
{
    [DbContext(typeof(AppContext))]
    [Migration("20220219120259_InitialDb")]
    partial class InitialDb
    {
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasAnnotation("ProductVersion", "6.0.1")
                .HasAnnotation("Relational:MaxIdentifierLength", 128);

            SqlServerModelBuilderExtensions.UseIdentityColumns(modelBuilder, 1L, 1);

            modelBuilder.Entity("BeatLeader_Server.Models.DifficultyDescription", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("int");

                    SqlServerPropertyBuilderExtensions.UseIdentityColumn(b.Property<int>("Id"), 1L, 1);

                    b.Property<int>("Bombs")
                        .HasColumnType("int");

                    b.Property<string>("DifficultyName")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<int>("Mode")
                        .HasColumnType("int");

                    b.Property<string>("ModeName")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<float>("Njs")
                        .HasColumnType("real");

                    b.Property<int>("Notes")
                        .HasColumnType("int");

                    b.Property<float>("Nps")
                        .HasColumnType("real");

                    b.Property<bool>("Ranked")
                        .HasColumnType("bit");

                    b.Property<string>("SongId")
                        .HasColumnType("nvarchar(450)");

                    b.Property<float?>("Stars")
                        .HasColumnType("real");

                    b.Property<int>("Value")
                        .HasColumnType("int");

                    b.Property<int>("Walls")
                        .HasColumnType("int");

                    b.HasKey("Id");

                    b.HasIndex("SongId");

                    b.ToTable("DifficultyDescription");
                });

            modelBuilder.Entity("BeatLeader_Server.Models.Leaderboard", b =>
                {
                    b.Property<string>("Id")
                        .HasColumnType("nvarchar(450)");

                    b.Property<int>("DifficultyId")
                        .HasColumnType("int");

                    b.Property<int>("Plays")
                        .HasColumnType("int");

                    b.Property<string>("SongId")
                        .HasColumnType("nvarchar(450)");

                    b.HasKey("Id");

                    b.HasIndex("DifficultyId");

                    b.HasIndex("SongId");

                    b.ToTable("Leaderboards");
                });

            modelBuilder.Entity("BeatLeader_Server.Models.Player", b =>
                {
                    b.Property<string>("Id")
                        .HasColumnType("nvarchar(450)");

                    b.Property<string>("Avatar")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<bool>("Banned")
                        .HasColumnType("bit");

                    b.Property<string>("Country")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<int>("CountryRank")
                        .HasColumnType("int");

                    b.Property<string>("Histories")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<bool>("Inactive")
                        .HasColumnType("bit");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("Platform")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<float>("Pp")
                        .HasColumnType("real");

                    b.Property<int>("Rank")
                        .HasColumnType("int");

                    b.Property<string>("Role")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<int>("ScoreStatsId")
                        .HasColumnType("int");

                    b.HasKey("Id");

                    b.HasIndex("ScoreStatsId");

                    b.ToTable("Players");
                });

            modelBuilder.Entity("BeatLeader_Server.Models.PlayerScoreStats", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("int");

                    SqlServerPropertyBuilderExtensions.UseIdentityColumn(b.Property<int>("Id"), 1L, 1);

                    b.Property<float>("AverageAccuracy")
                        .HasColumnType("real");

                    b.Property<float>("AverageRankedAccuracy")
                        .HasColumnType("real");

                    b.Property<int>("RankedPlayCount")
                        .HasColumnType("int");

                    b.Property<int>("ReplaysWatched")
                        .HasColumnType("int");

                    b.Property<int>("TotalPlayCount")
                        .HasColumnType("int");

                    b.Property<int>("TotalScore")
                        .HasColumnType("int");

                    b.HasKey("Id");

                    b.ToTable("Stats");
                });

            modelBuilder.Entity("BeatLeader_Server.Models.Playlist", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("int");

                    SqlServerPropertyBuilderExtensions.UseIdentityColumn(b.Property<int>("Id"), 1L, 1);

                    b.Property<bool>("IsShared")
                        .HasColumnType("bit");

                    b.Property<string>("Link")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("OwnerId")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("UserId")
                        .HasColumnType("nvarchar(450)");

                    b.HasKey("Id");

                    b.HasIndex("UserId");

                    b.ToTable("Playlists");
                });

            modelBuilder.Entity("BeatLeader_Server.Models.ReplayIdentification", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<byte[]>("Order")
                        .IsRequired()
                        .HasColumnType("varbinary(max)");

                    b.Property<byte[]>("Value")
                        .IsRequired()
                        .HasColumnType("varbinary(max)");

                    b.HasKey("Id");

                    b.ToTable("ReplayIdentification");
                });

            modelBuilder.Entity("BeatLeader_Server.Models.Score", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("int");

                    SqlServerPropertyBuilderExtensions.UseIdentityColumn(b.Property<int>("Id"), 1L, 1);

                    b.Property<float>("Accuracy")
                        .HasColumnType("real");

                    b.Property<int>("BadCuts")
                        .HasColumnType("int");

                    b.Property<int>("BaseScore")
                        .HasColumnType("int");

                    b.Property<int>("BombCuts")
                        .HasColumnType("int");

                    b.Property<int>("CountryRank")
                        .HasColumnType("int");

                    b.Property<bool>("FullCombo")
                        .HasColumnType("bit");

                    b.Property<int>("Hmd")
                        .HasColumnType("int");

                    b.Property<Guid>("IdentificationId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<string>("LeaderboardId")
                        .IsRequired()
                        .HasColumnType("nvarchar(450)");

                    b.Property<int>("MissedNotes")
                        .HasColumnType("int");

                    b.Property<int>("ModifiedScore")
                        .HasColumnType("int");

                    b.Property<string>("Modifiers")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<int>("Pauses")
                        .HasColumnType("int");

                    b.Property<string>("PlayerId")
                        .IsRequired()
                        .HasColumnType("nvarchar(450)");

                    b.Property<float>("Pp")
                        .HasColumnType("real");

                    b.Property<int>("Rank")
                        .HasColumnType("int");

                    b.Property<string>("Replay")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("Timeset")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<int>("WallsHit")
                        .HasColumnType("int");

                    b.Property<float>("Weight")
                        .HasColumnType("real");

                    b.HasKey("Id");

                    b.HasIndex("IdentificationId");

                    b.HasIndex("LeaderboardId");

                    b.HasIndex("PlayerId");

                    b.ToTable("Scores");
                });

            modelBuilder.Entity("BeatLeader_Server.Models.Song", b =>
                {
                    b.Property<string>("Id")
                        .HasColumnType("nvarchar(450)");

                    b.Property<string>("Author")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<double>("Bpm")
                        .HasColumnType("float");

                    b.Property<string>("CoverImage")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("Description")
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("DownloadUrl")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<double>("Duration")
                        .HasColumnType("float");

                    b.Property<string>("Hash")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("Mapper")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("SubName")
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("Tags")
                        .HasColumnType("nvarchar(max)");

                    b.HasKey("Id");

                    b.ToTable("Songs");
                });

            modelBuilder.Entity("BeatLeader_Server.Models.User", b =>
                {
                    b.Property<string>("Id")
                        .HasColumnType("nvarchar(450)");

                    b.Property<bool>("CustomAvatar")
                        .HasColumnType("bit");

                    b.Property<string>("PlayerId")
                        .IsRequired()
                        .HasColumnType("nvarchar(450)");

                    b.HasKey("Id");

                    b.HasIndex("PlayerId");

                    b.ToTable("Users");
                });

            modelBuilder.Entity("BeatLeader_Server.Models.DifficultyDescription", b =>
                {
                    b.HasOne("BeatLeader_Server.Models.Song", null)
                        .WithMany("Difficulties")
                        .HasForeignKey("SongId");
                });

            modelBuilder.Entity("BeatLeader_Server.Models.Leaderboard", b =>
                {
                    b.HasOne("BeatLeader_Server.Models.DifficultyDescription", "Difficulty")
                        .WithMany()
                        .HasForeignKey("DifficultyId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.HasOne("BeatLeader_Server.Models.Song", "Song")
                        .WithMany()
                        .HasForeignKey("SongId");

                    b.Navigation("Difficulty");

                    b.Navigation("Song");
                });

            modelBuilder.Entity("BeatLeader_Server.Models.Player", b =>
                {
                    b.HasOne("BeatLeader_Server.Models.PlayerScoreStats", "ScoreStats")
                        .WithMany()
                        .HasForeignKey("ScoreStatsId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("ScoreStats");
                });

            modelBuilder.Entity("BeatLeader_Server.Models.Playlist", b =>
                {
                    b.HasOne("BeatLeader_Server.Models.User", null)
                        .WithMany("Playlists")
                        .HasForeignKey("UserId");
                });

            modelBuilder.Entity("BeatLeader_Server.Models.Score", b =>
                {
                    b.HasOne("BeatLeader_Server.Models.ReplayIdentification", "Identification")
                        .WithMany()
                        .HasForeignKey("IdentificationId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.HasOne("BeatLeader_Server.Models.Leaderboard", "Leaderboard")
                        .WithMany("Scores")
                        .HasForeignKey("LeaderboardId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.HasOne("BeatLeader_Server.Models.Player", "Player")
                        .WithMany()
                        .HasForeignKey("PlayerId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("Identification");

                    b.Navigation("Leaderboard");

                    b.Navigation("Player");
                });

            modelBuilder.Entity("BeatLeader_Server.Models.User", b =>
                {
                    b.HasOne("BeatLeader_Server.Models.Player", "Player")
                        .WithMany()
                        .HasForeignKey("PlayerId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("Player");
                });

            modelBuilder.Entity("BeatLeader_Server.Models.Leaderboard", b =>
                {
                    b.Navigation("Scores");
                });

            modelBuilder.Entity("BeatLeader_Server.Models.Song", b =>
                {
                    b.Navigation("Difficulties");
                });

            modelBuilder.Entity("BeatLeader_Server.Models.User", b =>
                {
                    b.Navigation("Playlists");
                });
#pragma warning restore 612, 618
        }
    }
}