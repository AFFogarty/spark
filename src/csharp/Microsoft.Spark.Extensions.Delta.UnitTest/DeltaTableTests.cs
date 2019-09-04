using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Spark.E2ETest;
using Microsoft.Spark.E2ETest.Utils;
using Microsoft.Spark.Extensions.Delta.Tables;
using Microsoft.Spark.Sql;
using Xunit;

namespace Microsoft.Spark.Extensions.Delta.UnitTest
{
    [Collection("Delta Tests")]
    public class DeltaTableTests
    {
        private readonly SparkSession _spark;

        public DeltaTableTests(SparkFixture fixture)
        {
            _spark = fixture.Spark;
        }

        /// <summary>
        /// Run the end-to-end scenario from the Delta Quickstart tutorial.
        /// </summary>
        /// <see cref="https://docs.delta.io/latest/quick-start.html"/>
        [Fact]
        public void TestTutorialScenario()
        {
            using TemporaryDirectory dir = new TemporaryDirectory();
            string path = Path.Combine(dir.Path, "delta-table");

            // Write data to a Delta table.
            DataFrame data = _spark.Range(0, 5);
            data.Write().Format("delta").Save(path);

            // Validate that data contains the the sequence [0 ... 4].
            ValidateTutorialDataFrame(Enumerable.Range(0, 5), data);

            // Create a second iteration of the table.
            data = _spark.Range(5, 10);
            data.Write().Format("delta").Mode("overwrite").Save(path);

            // Load the data into a DeltaTable object.
            var deltaTable = DeltaTable.ForPath(path);

            // Validate that deltaTable contains the the sequence [5 ... 9].
            ValidateTutorialDataFrame(Enumerable.Range(5, 5), deltaTable.ToDF());

            //// Update every even value by adding 100 to it.
            //deltaTable.Update(
            //    condition: Functions.Expr("id % 2 == 0"),
            //    set: new Dictionary<string, Column>()
            //    {
            //        { "id", Functions.Expr("id + 100") }
            //    });

            //// Validate that deltaTable contains the the data:
            //// +---+
            //// | id|
            //// +---+
            //// |  5|
            //// |  7|
            //// |  9|
            //// |106|
            //// |108|
            //// +---+
            //ValidateTutorialDataFrame(new List<int>() { 5, 7, 9, 106, 108 }, deltaTable.ToDF());

            // Delete every even value.
            deltaTable.Delete(condition: Functions.Expr("id % 2 == 0"));

            // Validate that deltaTable contains:
            // +---+
            // | id|
            // +---+
            // |  5|
            // |  7|
            // |  9|
            // +---+
            ValidateTutorialDataFrame(
                new List<int>() { 5, 7, 9 },
                deltaTable.ToDF());

            // Upsert (merge) new data.
            DataFrame newData = _spark.Range(0, 20).As("newData").ToDF();

            deltaTable.As("oldData")
                .Merge(newData, "oldData.id = newData.id")
                .WhenMatched()
                .Update(
                    new Dictionary<string, Column>() {
                                { "id", Functions.Col("newData.id") }
                    })
                .WhenNotMatched()
                .InsertExpr(
                    new Dictionary<string, string>() {
                                { "id", "newData.id" }
                    })
                .Execute();

            // Validate that the resulTable contains the the sequence [0 ... 19].
            ValidateTutorialDataFrame(Enumerable.Range(0, 20), deltaTable.ToDF());
        }

        /// <summary>
        /// Test that methods return the expected signature.
        /// </summary>
        [Fact]
        public void TestSignatures()
        {
            using TemporaryDirectory dir = new TemporaryDirectory();
            string path = Path.Combine(dir.Path, "delta-table");

            DataFrame rangeRate = _spark.Range(15);
            rangeRate.Write().Format("delta").Save(path);

            DeltaTable table = DeltaTable.ForPath(path);

            Assert.IsType<DeltaTable>(table.As("oldTable"));

            Assert.IsType<DataFrame>(table.History());
            Assert.IsType<DataFrame>(table.History(200));

            DataFrame newTable = _spark.Range(10, 15).As("newTable");
            Assert.IsType<DeltaMergeBuilder>(
                table.Merge(newTable, Functions.Exp("oldTable.id == newTable.id")));
            Assert.IsType<DeltaMergeBuilder>(
                table.Merge(newTable, "oldTable.id == newTable.id"));

            Assert.IsType<DataFrame>(table.ToDF());

            // Update and UpdateExpr should return void.
            table.Update(new Dictionary<string, Column>() { });
            table.Update(Functions.Expr("id % 2 == 0"), new Dictionary<string, Column>() { });
            table.UpdateExpr(new Dictionary<string, string>() { });
            table.UpdateExpr("id % 2 == 1", new Dictionary<string, string>() { });

            Assert.IsType<DataFrame>(table.Vacuum());
            Assert.IsType<DataFrame>(table.Vacuum(168));

            // Delete should return void.
            table.Delete("id > 10");
            table.Delete(Functions.Expr("id > 5"));
            table.Delete();
        }

        /// <summary>
        /// Validate that a tutorial DataFrame contains only the expected values.
        /// </summary>
        /// <param name="expectedValues"></param>
        /// <param name="df"></param>
        private void ValidateTutorialDataFrame(IEnumerable<int> expectedValues, DataFrame df)
        {
            long expectedCount = expectedValues.Count();
            long dfCount = df.Count();

            Assert.Equal(expectedCount, dfCount);

            List<int> expectedList = new List<int>(expectedValues);
            IEnumerable<Row> dfCollection = df.Collect();
            List<int> dfList = new List<int>(dfCollection.Select(r => Convert.ToInt32(r.Get("id"))));

            HashSet<int> expectedSet = new HashSet<int>(expectedList);
            HashSet<int> resultSet = new HashSet<int>(dfList);

            Assert.True(expectedSet.SetEquals(resultSet));
        }
    }

    [CollectionDefinition("Delta Tests")]
    public class SparkCollection : ICollectionFixture<SparkFixture>
    {
        // This class has no code, and is never created. Its purpose is simply
        // to be the place to apply [CollectionDefinition] and all the
        // ICollectionFixture<> interfaces.
    }
}