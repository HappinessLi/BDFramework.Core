using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BDFramework.Asset;
using BDFramework.Core.Tools;
using BDFramework.Editor.AssetBundle;
using BDFramework.Editor.BuildPipeline.AssetBundle;
using BDFramework.ResourceMgr;
using BDFramework.Sql;
using BDFramework.VersionController;
using ServiceStack.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.AssetGraph;
using UnityEngine.AssetGraph.DataModel.Version2;

namespace BDFramework.Editor.AssetGraph.Node
{
    /// <summary>
    /// 颗粒度,不修改 只作为连线查看用 避免线到一坨了
    /// </summary>
    [CustomNode("BDFramework/[分包]设置分包名", 111)]
    public class MultiplePackageName : UnityEngine.AssetGraph.Node
    {
        /// <summary>
        /// 构建的上下文信息
        /// </summary>
        public AssetBundleBuildingContext BuildingCtx { get; set; }

        public override string ActiveStyle
        {
            get { return "node 3 on"; }
        }

        public override string InactiveStyle
        {
            get { return "node 3"; }
        }

        public override string Category
        {
            get { return "[分包]设置分包名"; }
        }


        public string PacakgeName = "";

        public override void Initialize(NodeData data)
        {
            data.AddDefaultInputPoint();
        }

        public override UnityEngine.AssetGraph.Node Clone(NodeData newData)
        {
            newData.AddDefaultInputPoint();

            return new MultiplePackageName();
        }


        public override void OnInspectorGUI(NodeGUI node, AssetReferenceStreamManager streamManager, NodeGUIInspector inspector, Action onValueChanged)
        {
            this.PacakgeName = EditorGUILayout.TextField("分包名:", this.PacakgeName);
            node.Name = "分包名:" + this.PacakgeName;
            //editor.UpdateNodeName(node);
        }

        /// <summary>
        /// 包目录列表
        /// </summary>
        private List<string> packageAssetList = new List<string>();

        /// <summary>
        /// 预览结果 编辑器连线数据，但是build模式也会执行
        /// 这里只建议设置BuildingCtx的ab颗粒度
        /// </summary>
        /// <param name="target"></param>
        /// <param name="nodeData"></param>
        /// <param name="incoming"></param>
        /// <param name="connectionsToOutput"></param>
        /// <param name="outputFunc"></param>
        public override void Prepare(BuildTarget target, NodeData nodeData, IEnumerable<PerformGraph.AssetGroups> incoming, IEnumerable<ConnectionData> connectionsToOutput, PerformGraph.Output outputFunc)
        {
            this.BuildingCtx = BDFrameworkAssetsEnv.BuildingCtx;
            if (incoming == null)
            {
                return;
            }

            //搜集所有的 asset reference 
            var comingAssetReferenceList = AssetGraphTools.GetComingAssets(incoming);
            if (comingAssetReferenceList.Count == 0)
            {
                return;
            }


            //
            packageAssetList = new List<string>();
            foreach (var ag in incoming)
            {
                foreach (var ags in ag.assetGroups)
                {
                    foreach (var ar in ags.Value)
                    {
                        packageAssetList.Add(ar.importFrom);
                    }
                }
            }
        }

        /// <summary>
        /// 保存分包设置
        /// </summary>
        /// <param name="ctx"></param>
        public override void Build(BuildTarget buildTarget, NodeData nodeData, IEnumerable<PerformGraph.AssetGroups> incoming, IEnumerable<ConnectionData> connectionsToOutput, PerformGraph.Output outputFunc,
            Action<NodeData, string, float> progressFunc)
        {
            var assetIdList = new List<int>();
            //寻找当前分包,包含的资源
            foreach (var assetPath in this.packageAssetList)
            {
                var buildAssetInfo = this.BuildingCtx.BuildAssetInfos.GetAssetInfo(assetPath);

                //依次把加入资源和依赖资源
                foreach (var dependGUID in buildAssetInfo.DependAssetList)
                {
                    var dependAssetInfo = this.BuildingCtx.BuildAssetInfos.AssetInfoMap.Values.First((ai) => ai.GUID.Equals(dependGUID));

                    //获取asset ab的idx
                    var idx = this.BuildingCtx.AssetBundleItemList.FindIndex((abi) => abi.AssetBundlePath == dependAssetInfo.ABName);
                    if (idx > -1)
                    {
                        assetIdList.Add(dependAssetInfo.ArtAssetsInfoIdx);
                    }
                    else
                    {
                        BDebug.LogError("分包依赖失败:" + dependGUID);
                    }
                }

                //符合分包路径
                assetIdList.Add(buildAssetInfo.ArtAssetsInfoIdx);
            }

            //新建package描述表
            var subPackage = new SubPackageConfigItem();
            subPackage.PackageName = this.PacakgeName;
            //热更资源
            subPackage.ArtAssetsIdList = assetIdList.Distinct().ToList();
            subPackage.ArtAssetsIdList.Sort();
            //热更代码
            subPackage.HotfixCodePathList.Add(ScriptLoder.DLL_PATH);
            //热更表格
            subPackage.TablePathList.Add(SqliteLoder.LOCAL_DB_PATH);
            //配置表
            subPackage.ConfAndInfoList.Add(BResources.ART_ASSET_INFO_PATH);
            subPackage.ConfAndInfoList.Add(BResources.ART_ASSET_TYPES_PATH);
            subPackage.ConfAndInfoList.Add(ClientAssetsHelper.PACKAGE_BUILD_INFO_PATH);

            MultiplePackage.AssetMultiplePackageConfigList.Add(subPackage);
            //
            var path = string.Format("{0}/{1}/{2}", this.BuildingCtx.BuildParams.OutputPath, BApplication.GetPlatformPath(buildTarget), BResources.ASSETS_SUB_PACKAGE_CONFIG_PATH);
            var csv = CsvSerializer.SerializeToString(MultiplePackage.AssetMultiplePackageConfigList);
            FileHelper.WriteAllText(path, csv);
            Debug.Log("保存分包设置:" + this.PacakgeName + " -" + buildTarget.ToString());
        }
    }
}
