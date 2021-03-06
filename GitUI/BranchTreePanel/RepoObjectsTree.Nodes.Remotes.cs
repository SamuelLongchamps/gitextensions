﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using GitCommands;
using GitCommands.Git;
using GitUI.CommandsDialogs;
using GitUI.HelperDialogs;
using GitUI.Properties;

namespace GitUI.BranchTreePanel
{
    public partial class RepoObjectsTree
    {
        private class RemoteBranchTree : Tree
        {
            public RemoteBranchTree(TreeNode treeNode, IGitUICommandsSource uiCommands)
                : base(treeNode, uiCommands)
            {
                uiCommands.GitUICommandsChanged += UiCommands_GitUICommandsChanged;
            }

            private void UiCommands_GitUICommandsChanged(object sender, GitUICommandsChangedEventArgs e)
            {
                TreeViewNode.TreeView.SelectedNode = null;
            }

            protected override void LoadNodes(CancellationToken token)
            {
                var nodes = new Dictionary<string, BaseBranchNode>();

                var branches = Module.GetRefs()
                    .Where(branch => branch.IsRemote && !branch.IsTag)
                    .OrderBy(r => r.Name)
                    .Select(branch => branch.Name);

                var remotes = Module.GetRemotes(allowEmpty: true);
                var branchFullPaths = new List<string>();
                foreach (var branchPath in branches)
                {
                    var remote = branchPath.Split('/').First();
                    if (!remotes.Contains(remote))
                    {
                        continue;
                    }

                    var remoteBranchNode = new RemoteBranchNode(this, branchPath);
                    var parent = remoteBranchNode.CreateRootNode(nodes,
                        (tree, parentPath) => CreateRemoteBranchPathNode(tree, parentPath, remote));
                    if (parent != null)
                    {
                        Nodes.AddNode(parent);
                    }

                    branchFullPaths.Add(remoteBranchNode.FullPath);
                }

                FireBranchAddedEvent(branchFullPaths);
            }

            private static BaseBranchNode CreateRemoteBranchPathNode(Tree tree,
                string parentPath, string remoteName)
            {
                if (parentPath == remoteName)
                {
                    return new RemoteRepoNode(tree, parentPath);
                }

                return new BasePathNode(tree, parentPath);
            }

            protected override void FillTreeViewNode()
            {
                base.FillTreeViewNode();
                TreeViewNode.Expand();
            }

            public void RenameRemote(string orgName, string newName)
            {
                var treeNode = FindRemoteRepoTreeNodeByName(orgName);
                if (treeNode == null)
                {
                    throw new InvalidOperationException("Cannot rename a non-existing remote");
                }

                treeNode.Text = newName;
                var remoteRepoNode = FindRemoteRepoNodeByName(orgName);
                remoteRepoNode.ChangeName(newName);
            }

            private RemoteRepoNode FindRemoteRepoNodeByName(string remoteName)
            {
                foreach (var node in Nodes)
                {
                    if (node.DisplayText() != remoteName)
                    {
                        continue;
                    }

                    if (node is RemoteRepoNode remoteRepoNode)
                    {
                        return remoteRepoNode;
                    }
                }

                return null;
            }

            private TreeNode FindRemoteRepoTreeNodeByName(string remoteName)
            {
                return TreeViewNode.Nodes.Cast<TreeNode>().FirstOrDefault(treeNode => treeNode.Text == remoteName);
            }

            public void DeleteRemote(string remoteName)
            {
                var treeNode = FindRemoteRepoTreeNodeByName(remoteName);
                if (treeNode == null)
                {
                    return;
                }

                TreeViewNode.Nodes.Remove(treeNode);
                var repoNode = FindRemoteRepoNodeByName(remoteName);
                Nodes.Remove(repoNode);
            }

            public void AddRemote(string remoteName)
            {
                Nodes.AddNode(new RemoteRepoNode(this, remoteName));
                Nodes.FillTreeViewNode(TreeViewNode);
            }
        }

        private sealed class RemoteBranchNode : BaseBranchNode
        {
            public RemoteBranchNode(Tree tree, string fullPath) : base(tree, fullPath)
            {
            }

            internal override void OnSelected()
            {
                base.OnSelected();
                SelectRevision();
            }

            public void Fetch()
            {
                var remoteBranchInfo = GetRemoteBranchInfo();
                var cmd = Module.FetchCmd(remoteBranchInfo.Remote, remoteBranchInfo.BranchName,
                    null, null);
                var ret = FormRemoteProcess.ShowDialog(TreeViewNode.TreeView, Module, cmd);
                if (ret)
                {
                    UICommands.RepoChangedNotifier.Notify();
                }
            }

            private struct RemoteBranchInfo
            {
                public string Remote { get; set; }

                public string BranchName { get; set; }
            }

            private RemoteBranchInfo GetRemoteBranchInfo()
            {
                var remote = FullPath.Split('/').First();
                var branch = FullPath.Substring(remote.Length + 1);
                return new RemoteBranchInfo
                {
                    Remote = remote, BranchName = branch
                };
            }

            public void CreateBranch()
            {
                UICommands.StartCreateBranchDialog(TreeViewNode.TreeView, new GitRevision(FullPath));
            }

            public void Delete()
            {
                var remoteBranchInfo = GetRemoteBranchInfo();
                var cmd = new GitDeleteRemoteBranchesCmd(remoteBranchInfo.Remote, new[] { remoteBranchInfo.BranchName });
                if (MessageBoxes.ConfirmDeleteRemoteBranch(TreeViewNode.TreeView,
                    remoteBranchInfo.BranchName, remoteBranchInfo.Remote))
                {
                    UICommands.StartCommandLineProcessDialog(cmd, null);
                }
            }

            public void Checkout()
            {
                using (var form = new FormCheckoutBranch(UICommands, FullPath, remote: true))
                {
                    form.ShowDialog(TreeViewNode.TreeView);
                }
            }

            internal override void OnDoubleClick()
            {
                Checkout();
            }

            public void Merge()
            {
                using (var form = new FormMergeBranch(UICommands, FullPath))
                {
                    form.ShowDialog(TreeViewNode.TreeView);
                }
            }

            public void Rebase()
            {
                using (var form = new FormRebase(UICommands, FullPath))
                {
                    form.ShowDialog(TreeViewNode.TreeView);
                }
            }

            public void Reset()
            {
                using (var form = new FormResetCurrentBranch(UICommands, new GitRevision(FullPath)))
                {
                    form.ShowDialog(TreeViewNode.TreeView);
                }
            }

            protected override void ApplyStyle()
            {
                base.ApplyStyle();
                TreeViewNode.ImageKey = TreeViewNode.SelectedImageKey = nameof(MsVsImages.BranchRemote_16x);
            }
        }

        private sealed class RemoteRepoNode : BaseBranchNode
        {
            public RemoteRepoNode(Tree tree, string fullPath) : base(tree, fullPath)
            {
            }

            public void Fetch()
            {
                var cmd = Module.FetchCmd(FullPath, null, null, null);
                var ret = FormRemoteProcess.ShowDialog(TreeViewNode.TreeView, Module, cmd);
                if (ret)
                {
                    UICommands.RepoChangedNotifier.Notify();
                }
            }

            protected override void ApplyStyle()
            {
                base.ApplyStyle();
                TreeViewNode.ImageKey = TreeViewNode.SelectedImageKey = nameof(MsVsImages.Repository_16x);
            }

            public void ChangeName(string newName)
            {
                Name = newName;
            }
        }
    }
}
