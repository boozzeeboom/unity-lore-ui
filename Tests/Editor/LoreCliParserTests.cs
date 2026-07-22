using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ProjectC.LoreUnity;

namespace ProjectC.LoreUnity.Tests.Editor
{
    public class LoreCliParserTests
    {
        // ── ParseStatus ──

        [Test]
        public void ParseStatus_BasicOutput_ReturnsCorrectStatus()
        {
            var input = @"Repository 019f861b853178819ccf31c6aa3b7900
On branch main revision 7 -> ba44c4b6eb4ece15feecd15893115734d3cd556c2991865bc737729663897b03
Remote revision 7 -> ba44c4b6eb4ece15feecd15893115734d3cd556c2991865bc737729663897b03
Local branch in sync with remote
Changes not staged for commit:
M Assets/Test/File1.asset
M Assets/Test/File2.unity";

            var result = LoreCliParser.ParseStatus(input);

            Assert.IsNotNull(result);
            Assert.AreEqual("019f861b853178819ccf31c6aa3b7900", result.RepositoryId);
            Assert.AreEqual("main", result.BranchName);
            Assert.AreEqual(7, result.CurrentRevision);
            Assert.AreEqual("ba44c4b6eb4ece15feecd15893115734d3cd556c2991865bc737729663897b03", result.CurrentSignature);
            Assert.AreEqual(7, result.RemoteRevision);
            Assert.IsTrue(result.IsSynced);
            Assert.AreEqual(2, result.Files.Count);
            Assert.AreEqual("Assets/Test/File1.asset", result.Files[0].Path);
            Assert.AreEqual(LoreFileStatusType.Modified, result.Files[0].Status);
        }

        [Test]
        public void ParseStatus_WithAddedDeleted_ParsesCorrectly()
        {
            var input = @"Repository abc123
On branch feature revision 5 -> sig1
Remote revision 3 -> sig2
Local branch is ahead of remote
Changes not staged for commit:
M Assets/Modified.asset
A Assets/Added.prefab
D Assets/Deleted.cs";

            var result = LoreCliParser.ParseStatus(input);

            Assert.IsNotNull(result);
            Assert.AreEqual("feature", result.BranchName);
            Assert.AreEqual(5, result.CurrentRevision);
            Assert.AreEqual(3, result.RemoteRevision);
            Assert.IsFalse(result.IsSynced);
            Assert.AreEqual(3, result.Files.Count);
            Assert.AreEqual(LoreFileStatusType.Modified, result.Files[0].Status);
            Assert.AreEqual(LoreFileStatusType.Added, result.Files[1].Status);
            Assert.AreEqual(LoreFileStatusType.Deleted, result.Files[2].Status);
        }

        [Test]
        public void ParseStatus_EmptyInput_ReturnsNull()
        {
            Assert.IsNull(LoreCliParser.ParseStatus(""));
            Assert.IsNull(LoreCliParser.ParseStatus(null));
        }

        [Test]
        public void ParseStatus_NoChanges_ReturnsStatusWithoutFiles()
        {
            var input = @"Repository abc
On branch main revision 3 -> sig
Remote revision 3 -> sig
Local branch in sync with remote
Changes not staged for commit:";

            var result = LoreCliParser.ParseStatus(input);
            Assert.IsNotNull(result);
            Assert.AreEqual(0, result.Files.Count);
            Assert.IsTrue(result.IsSynced);
        }

        // ── ParseHistory ──

        [Test]
        public void ParseHistory_SingleCommit_ReturnsOneEntry()
        {
            var input = @"Revision  : 7
Signature : ba44c4b6eb4ece15feecd15893115734d3cd556c2991865bc737729663897b03
Branch    : e726318bbc3fd75ac8733a7e030cc35b
Date      : Wed, 22 Jul 2026 05:29:39 +0000
    Commit message here
    Second line of message";

            var result = LoreCliParser.ParseHistory(input);

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(7, result[0].RevisionNumber);
            Assert.AreEqual("ba44c4b6eb4ece15feecd15893115734d3cd556c2991865bc737729663897b03", result[0].Signature);
            Assert.AreEqual("e726318bbc3fd75ac8733a7e030cc35b", result[0].BranchId);
            Assert.AreEqual("Commit message here\nSecond line of message", result[0].Message);
        }

        [Test]
        public void ParseHistory_MultipleCommits_ReturnsAll()
        {
            var input = @"Revision  : 7
Signature : sig7
Branch    : branch1
Date      : Wed, 22 Jul 2026 05:29:39 +0000
    Commit 7

Revision  : 6
Signature : sig6
Branch    : branch1
Date      : Wed, 22 Jul 2026 04:55:08 +0000
    Commit 6";

            var result = LoreCliParser.ParseHistory(input);

            Assert.AreEqual(2, result.Count);
            Assert.AreEqual(7, result[0].RevisionNumber);
            Assert.AreEqual("Commit 7", result[0].Message);
            Assert.AreEqual(6, result[1].RevisionNumber);
            Assert.AreEqual("Commit 6", result[1].Message);
        }

        [Test]
        public void ParseHistory_EmptyInput_ReturnsEmptyList()
        {
            var result = LoreCliParser.ParseHistory("");
            Assert.IsNotNull(result);
            Assert.AreEqual(0, result.Count);
        }

        [Test]
        public void ParseHistory_WithParent_ParsesParent()
        {
            var input = @"Revision  : 7
Signature : sig7
Parent    : sig6
Branch    : branch1
Date      : Wed, 22 Jul 2026 05:29:39 +0000
    Commit message";

            var result = LoreCliParser.ParseHistory(input);

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("sig6", result[0].ParentSignature);
        }

        // ── ParseHistoryOneLine ──

        [Test]
        public void ParseHistoryOneLine_Basic_ReturnsParsed()
        {
            var input = @"7 Commit message here
6 Another commit
5 Third one";

            var result = LoreCliParser.ParseHistoryOneLine(input);

            Assert.AreEqual(3, result.Count);
            Assert.AreEqual(7, result[0].RevisionNumber);
            Assert.AreEqual("Commit message here", result[0].Message);
            Assert.AreEqual(6, result[1].RevisionNumber);
            Assert.AreEqual("Another commit", result[1].Message);
        }

        [Test]
        public void ParseHistoryOneLine_Empty_ReturnsEmpty()
        {
            var result = LoreCliParser.ParseHistoryOneLine("");
            Assert.AreEqual(0, result.Count);
        }

        // ── ParseBranchList ──

        [Test]
        public void ParseBranchList_LocalAndRemote_ReturnsBoth()
        {
            var input = @"Local branches:
* main
  feature
Remote branches:
  main
  feature";

            var result = LoreCliParser.ParseBranchList(input);

            Assert.AreEqual(4, result.Count);
            var localMain = result[0];
            Assert.AreEqual("main", localMain.Name);
            Assert.IsTrue(localMain.IsCurrent);
            Assert.IsFalse(localMain.IsRemote);

            var localFeature = result[1];
            Assert.AreEqual("feature", localFeature.Name);
            Assert.IsFalse(localFeature.IsCurrent);
            Assert.IsFalse(localFeature.IsRemote);

            var remoteMain = result[2];
            Assert.AreEqual("main", remoteMain.Name);
            Assert.IsTrue(remoteMain.IsRemote);

            var remoteFeature = result[3];
            Assert.AreEqual("feature", remoteFeature.Name);
            Assert.IsTrue(remoteFeature.IsRemote);
        }

        [Test]
        public void ParseBranchList_SingleBranch_ReturnsOne()
        {
            var input = @"Local branches:
* main";

            var result = LoreCliParser.ParseBranchList(input);
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("main", result[0].Name);
            Assert.IsTrue(result[0].IsCurrent);
        }

        // ── ParseBranchInfo ──

        [Test]
        public void ParseBranchInfo_ReturnsCorrectData()
        {
            var input = @"Branch main
  ID: e726318bbc3fd75ac8733a7e030cc35b
  Latest: ba44c4b6eb4ece15feecd15893115734d3cd556c2991865bc737729663897b03
  Remote Latest: ba44c4b6eb4ece15feecd15893115734d3cd556c2991865bc737729663897b03
  Created: Tue, 21 Jul 2026 19:16:18 +0000";

            var result = LoreCliParser.ParseBranchInfo(input);

            Assert.IsNotNull(result);
            Assert.AreEqual("main", result.Name);
            Assert.AreEqual("e726318bbc3fd75ac8733a7e030cc35b", result.Id);
            Assert.AreEqual("ba44c4b6eb4ece15feecd15893115734d3cd556c2991865bc737729663897b03", result.LatestSignature);
            Assert.AreEqual(result.LatestSignature, result.RemoteLatestSignature);
            Assert.AreEqual(2026, result.Created.Year);
        }

        // ── ParseRepositoryInfo ──

        [Test]
        public void ParseRepositoryInfo_ReturnsCorrectData()
        {
            var input = @"project-c (019f861b853178819ccf31c6aa3b7900)

Remote URL: lore://127.0.0.1:41337
Default branch: main (e726318bbc3fd75ac8733a7e030cc35b)
Creator: <unknown>
Created: Wed, 21 Jan 1970 15:44:21 +0000";

            var result = LoreCliParser.ParseRepositoryInfo(input);

            Assert.IsNotNull(result);
            Assert.AreEqual("project-c", result.Name);
            Assert.AreEqual("019f861b853178819ccf31c6aa3b7900", result.Id);
            Assert.AreEqual("lore://127.0.0.1:41337", result.RemoteUrl);
            Assert.AreEqual("main", result.DefaultBranch);
        }

        // ── ParseRevisionInfo ──

        [Test]
        public void ParseRevisionInfo_ReturnsCorrectData()
        {
            var input = @"Revision  : 7
Signature : ba44c4b6eb4ece15feecd15893115734d3cd556c2991865bc737729663897b03
Parent    : d2902e808844ce137dece92b52c9b2fafd8da789cca69a59fb6d35a15695d6ea
Branch    : e726318bbc3fd75ac8733a7e030cc35b
Date      : Wed, 22 Jul 2026 05:29:39 +0000
    Commit message text";

            var result = LoreCliParser.ParseRevisionInfo(input);

            Assert.IsNotNull(result);
            Assert.AreEqual(7, result.RevisionNumber);
            Assert.AreEqual("ba44c4b6eb4ece15feecd15893115734d3cd556c2991865bc737729663897b03", result.Signature);
            Assert.AreEqual("d2902e808844ce137dece92b52c9b2fafd8da789cca69a59fb6d35a15695d6ea", result.ParentSignature);
            Assert.AreEqual("Commit message text", result.Message);
        }

        // ── ParseDiffFiles ──

        [Test]
        public void ParseDiffFiles_ReturnsFiles()
        {
            var input = @"--- a/file1.cs
+++ b/file1.cs
@@ -1,5 +1,7 @@
-old line
+new line
--- b/file2.cs
+++ b/file2.cs";

            var result = LoreCliParser.ParseDiffFiles(input);

            Assert.AreEqual(2, result.Count);
            Assert.AreEqual("b/file1.cs", result[0]);
            Assert.AreEqual("b/file2.cs", result[1]);
        }

        [Test]
        public void ParseDiffFiles_Empty_ReturnsEmpty()
        {
            var result = LoreCliParser.ParseDiffFiles("");
            Assert.AreEqual(0, result.Count);
        }

        // ── Edge cases ──

        [Test]
        public void ParseHistory_MalformedBlock_SkipsWithoutCrash()
        {
            var input = @"Revision  : 7
Signature : sig7
Date      : Wed, 22 Jul 2026 05:29:39 +0000
    Message

Some garbage text

Revision  : 6
Signature : sig6
Date      : Wed, 22 Jul 2026 04:55:08 +0000
    Another message";

            // Should not throw; should return valid entries
            var result = LoreCliParser.ParseHistory(input);
            Assert.IsTrue(result.Count > 0);
            // At minimum, should parse the two valid blocks
            Assert.IsTrue(result.Any(c => c.RevisionNumber == 7));
            Assert.IsTrue(result.Any(c => c.RevisionNumber == 6));
        }

        [Test]
        public void ParseStatus_FilenameWithSpaces_ParsesCorrectly()
        {
            var input = @"Repository abc
On branch main revision 1 -> sig
Remote revision 1 -> sig
Local branch in sync with remote
Changes not staged for commit:
M Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF - Fallback.asset";

            var result = LoreCliParser.ParseStatus(input);
            Assert.IsNotNull(result);
            Assert.AreEqual(1, result.Files.Count);
            Assert.IsTrue(result.Files[0].Path.Contains("LiberationSans SDF - Fallback.asset"));
        }
    }
}
