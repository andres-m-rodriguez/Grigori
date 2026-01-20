using Grigori.Infrastructure.Services.Search;

namespace Grigori.Tests;

/// <summary>
/// Tests for ReciprocalRankFusion to verify it correctly handles edge cases
/// where only one search method returns results.
/// </summary>
public class ReciprocalRankFusionTests
{
    [Fact]
    public void Fuse_WithEmptyFirstList_ReturnsSecondListResults()
    {
        // Arrange
        var emptyList = Array.Empty<int>();
        var secondList = new[] { 1, 2, 3 };

        // Act
        var result = ReciprocalRankFusion.Fuse<int>([emptyList, secondList]);

        // Assert
        Assert.Equal(3, result.Count);
        Assert.Equal(1, result[0].Id);
        Assert.Equal(2, result[1].Id);
        Assert.Equal(3, result[2].Id);
    }

    [Fact]
    public void Fuse_WithEmptySecondList_ReturnsFirstListResults()
    {
        // Arrange
        var firstList = new[] { 1, 2, 3 };
        var emptyList = Array.Empty<int>();

        // Act
        var result = ReciprocalRankFusion.Fuse<int>([firstList, emptyList]);

        // Assert
        Assert.Equal(3, result.Count);
        Assert.Equal(1, result[0].Id);
        Assert.Equal(2, result[1].Id);
        Assert.Equal(3, result[2].Id);
    }

    [Fact]
    public void Fuse_WithBothListsEmpty_ReturnsEmptyResult()
    {
        // Arrange
        var emptyList1 = Array.Empty<int>();
        var emptyList2 = Array.Empty<int>();

        // Act
        var result = ReciprocalRankFusion.Fuse<int>([emptyList1, emptyList2]);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void FuseWeighted_WithEmptyFirstList_ReturnsSecondListResults()
    {
        // Arrange
        var emptyList = Array.Empty<int>();
        var secondList = new[] { 10, 20, 30 };

        // Act
        var result = ReciprocalRankFusion.FuseWeighted(emptyList, secondList);

        // Assert
        Assert.Equal(3, result.Count);
        Assert.Equal(10, result[0].Id);
        Assert.Equal(20, result[1].Id);
        Assert.Equal(30, result[2].Id);
    }

    [Fact]
    public void FuseWeighted_WithEmptySecondList_ReturnsFirstListResults()
    {
        // Arrange
        var firstList = new[] { 10, 20, 30 };
        var emptyList = Array.Empty<int>();

        // Act
        var result = ReciprocalRankFusion.FuseWeighted(firstList, emptyList);

        // Assert
        Assert.Equal(3, result.Count);
        Assert.Equal(10, result[0].Id);
        Assert.Equal(20, result[1].Id);
        Assert.Equal(30, result[2].Id);
    }

    [Fact]
    public void FuseWeighted_WithBothListsEmpty_ReturnsEmptyResult()
    {
        // Arrange
        var emptyList1 = Array.Empty<int>();
        var emptyList2 = Array.Empty<int>();

        // Act
        var result = ReciprocalRankFusion.FuseWeighted(emptyList1, emptyList2);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void FuseWithScores_WithEmptySemanticResults_ReturnsLexicalResults()
    {
        // Arrange
        var semanticResults = Array.Empty<(int Id, float Score)>();
        var lexicalResults = new[]
        {
            (Id: 1, Score: 0.9),
            (Id: 2, Score: 0.7),
            (Id: 3, Score: 0.5)
        };

        // Act
        var result = ReciprocalRankFusion.FuseWithScores(semanticResults, lexicalResults);

        // Assert
        Assert.Equal(3, result.Count);
        Assert.Equal(1, result[0].Id);
        Assert.Equal(2, result[1].Id);
        Assert.Equal(3, result[2].Id);
        // Verify these came only from lexical (not in both lists)
        Assert.All(result, r => Assert.False(r.InBothLists));
        Assert.All(result, r => Assert.Equal(0, r.SemanticScore));
    }

    [Fact]
    public void FuseWithScores_WithEmptyLexicalResults_ReturnsSemanticResults()
    {
        // Arrange
        var semanticResults = new[]
        {
            (Id: 1, Score: 0.95f),
            (Id: 2, Score: 0.85f),
            (Id: 3, Score: 0.75f)
        };
        var lexicalResults = Array.Empty<(int Id, double Score)>();

        // Act
        var result = ReciprocalRankFusion.FuseWithScores(semanticResults, lexicalResults);

        // Assert
        Assert.Equal(3, result.Count);
        Assert.Equal(1, result[0].Id);
        Assert.Equal(2, result[1].Id);
        Assert.Equal(3, result[2].Id);
        // Verify these came only from semantic (not in both lists)
        Assert.All(result, r => Assert.False(r.InBothLists));
        Assert.All(result, r => Assert.Equal(0, r.LexicalScore));
    }

    [Fact]
    public void FuseWithScores_WithBothListsEmpty_ReturnsEmptyResult()
    {
        // Arrange
        var semanticResults = Array.Empty<(int Id, float Score)>();
        var lexicalResults = Array.Empty<(int Id, double Score)>();

        // Act
        var result = ReciprocalRankFusion.FuseWithScores(semanticResults, lexicalResults);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void FuseWithScores_WithOverlappingResults_MarksAsInBothLists()
    {
        // Arrange
        var semanticResults = new[]
        {
            (Id: 1, Score: 0.9f),
            (Id: 2, Score: 0.8f)
        };
        var lexicalResults = new[]
        {
            (Id: 2, Score: 0.85),
            (Id: 3, Score: 0.7)
        };

        // Act
        var result = ReciprocalRankFusion.FuseWithScores(semanticResults, lexicalResults);

        // Assert
        Assert.Equal(3, result.Count);

        var item2 = result.First(r => r.Id == 2);
        Assert.True(item2.InBothLists);
        Assert.Equal(0.8f, item2.SemanticScore);
        Assert.Equal(0.85, item2.LexicalScore);

        var item1 = result.First(r => r.Id == 1);
        Assert.False(item1.InBothLists);
        Assert.Equal(0.9f, item1.SemanticScore);
        Assert.Equal(0, item1.LexicalScore);

        var item3 = result.First(r => r.Id == 3);
        Assert.False(item3.InBothLists);
        Assert.Equal(0, item3.SemanticScore);
        Assert.Equal(0.7, item3.LexicalScore);
    }

    [Fact]
    public void FuseWithScores_ItemInBothLists_HasHigherFusedScore()
    {
        // Arrange - Item 2 appears in both lists
        var semanticResults = new[]
        {
            (Id: 1, Score: 0.9f),
            (Id: 2, Score: 0.8f)
        };
        var lexicalResults = new[]
        {
            (Id: 2, Score: 0.85),
            (Id: 3, Score: 0.9)
        };

        // Act
        var result = ReciprocalRankFusion.FuseWithScores(semanticResults, lexicalResults);

        // Assert - Item 2 should have highest fused score because it's in both lists
        Assert.Equal(2, result[0].Id);
        Assert.True(result[0].InBothLists);
    }

    [Fact]
    public void Fuse_PreservesRankOrdering_FromSingleList()
    {
        // Arrange - only one list with specific ordering
        var rankedList = new[] { 100, 200, 300, 400, 500 };
        var emptyList = Array.Empty<int>();

        // Act
        var result = ReciprocalRankFusion.Fuse<int>([rankedList, emptyList]);

        // Assert - order should be preserved
        Assert.Equal(5, result.Count);
        for (int i = 0; i < 5; i++)
        {
            Assert.Equal(rankedList[i], result[i].Id);
        }
    }

    [Fact]
    public void FuseWithScores_WithWeights_AppliesCorrectly()
    {
        // Arrange - same items in both lists, different weights
        var semanticResults = new[] { (Id: 1, Score: 0.9f) };
        var lexicalResults = new[] { (Id: 2, Score: 0.9) };
        var semanticWeight = 2.0;
        var lexicalWeight = 1.0;

        // Act
        var result = ReciprocalRankFusion.FuseWithScores(
            semanticResults,
            lexicalResults,
            semanticWeight,
            lexicalWeight);

        // Assert - semantic result should have higher fused score due to weight
        Assert.Equal(2, result.Count);
        Assert.Equal(1, result[0].Id); // Semantic result first due to higher weight
        Assert.True(result[0].FusedScore > result[1].FusedScore);
    }
}
