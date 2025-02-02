﻿using Microsoft.Win32.SafeHandles;
using RX_Explorer.Interface;
using ShareClassLibrary;
using System;
using System.IO;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media.Imaging;

namespace RX_Explorer.Class
{
    public class FileSystemStorageFile : FileSystemStorageItemBase, ICoreStorageItem<StorageFile>
    {
        public override string Type => string.IsNullOrEmpty(base.Type) ? Globalization.GetString("File_Admin_DisplayType") : base.Type;

        public override string DisplayType => (StorageItem?.DisplayType) ?? Type;

        public override string DisplayName => Name;

        public override string SizeDescription => Size.GetSizeDescription();

        public override bool IsReadOnly
        {
            get
            {
                if (StorageItem == null)
                {
                    return base.IsReadOnly;
                }
                else
                {
                    return StorageItem.Attributes.HasFlag(Windows.Storage.FileAttributes.ReadOnly);
                }
            }
        }

        public override bool IsSystemItem
        {
            get
            {
                if (StorageItem == null)
                {
                    return base.IsSystemItem;
                }
                else
                {
                    return false;
                }
            }
        }

        private static readonly Uri Const_File_Image_Uri = AppThemeController.Current.Theme == ElementTheme.Dark
                                                                ? new Uri("ms-appx:///Assets/Page_Solid_White.png")
                                                                : new Uri("ms-appx:///Assets/Page_Solid_Black.png");

        public override BitmapImage Thumbnail => base.Thumbnail ?? new BitmapImage(Const_File_Image_Uri);

        public StorageFile StorageItem { get; protected set; }

        public FileSystemStorageFile(StorageFile Item) : base(Item.Path, Item.GetSafeFileHandle(AccessMode.Read, OptimizeOption.None), false)
        {
            StorageItem = Item;
        }

        public FileSystemStorageFile(NativeFileData Data) : base(Data)
        {

        }

        public FileSystemStorageFile(MTPFileData Data) : base(Data)
        {

        }

        public async virtual Task<Stream> GetStreamFromFileAsync(AccessMode Mode, OptimizeOption Option)
        {
            if (NativeWin32API.CreateStreamFromFile(Path, Mode, Option) is FileStream Stream)
            {
                return Stream;
            }
            else
            {
                FileAccess Access = Mode switch
                {
                    AccessMode.Read => FileAccess.Read,
                    AccessMode.ReadWrite or AccessMode.Exclusive => FileAccess.ReadWrite,
                    AccessMode.Write => FileAccess.Write,
                    _ => throw new NotSupportedException()
                };

                SafeFileHandle Handle = await GetNativeHandleAsync(Mode, Option);

                if ((Handle?.IsInvalid).GetValueOrDefault(true))
                {
                    throw new UnauthorizedAccessException($"Could not create a new file stream, Path: \"{Path}\"");
                }
                else
                {
                    return new FileStream(Handle, Access, 4096, true);
                }
            }
        }

        public virtual async Task<ulong> GetSizeOnDiskAsync()
        {
            async Task<ulong> GetSizeOnDiskCoreAsync()
            {
                using (SafeFileHandle Handle = await GetNativeHandleAsync(AccessMode.Read, OptimizeOption.None))
                {
                    if (!Handle.IsInvalid)
                    {
                        string PathRoot = System.IO.Path.GetPathRoot(Path);

                        if (!string.IsNullOrEmpty(PathRoot))
                        {
                            if (NativeWin32API.GetDiskFreeSpace(PathRoot.TrimEnd('\\'), out uint SectorsPerCluster, out uint BytesPerSector, out _, out _))
                            {
                                ulong ClusterSize = Convert.ToUInt64(SectorsPerCluster) * Convert.ToUInt64(BytesPerSector);
                                ulong CompressedSize = NativeWin32API.GetAllocationSizeFromHandle(Handle.DangerousGetHandle());

                                if (ClusterSize > 0)
                                {
                                    if (CompressedSize % ClusterSize > 0)
                                    {
                                        return CompressedSize + ClusterSize - CompressedSize % ClusterSize;
                                    }
                                    else
                                    {
                                        return CompressedSize;
                                    }
                                }
                            }
                        }
                    }
                }

                return 0;
            }

            try
            {
                ulong SizeOnDisk = await GetSizeOnDiskCoreAsync();

                if (SizeOnDisk > 0)
                {
                    return SizeOnDisk;
                }
                else
                {
                    using (FullTrustProcessController.ExclusiveUsage Exlusive = await FullTrustProcessController.GetAvailableControllerAsync())
                    {
                        return await Exlusive.Controller.GetSizeOnDiskAsync(Path);
                    }
                }
            }
            catch (Exception ex)
            {
                LogTracer.Log(ex, "Could not get the size on disk");
            }

            return 0;
        }

        protected override async Task LoadCoreAsync(bool ForceUpdate)
        {
            if (ForceUpdate)
            {
                try
                {
                    NativeFileData Data = NativeWin32API.GetStorageItemRawData(Path);

                    if (Data.IsDataValid)
                    {
                        ModifiedTime = Data.ModifiedTime;
                        Size = Data.Size;
                    }
                    else if (await GetStorageItemAsync() is StorageFile File)
                    {
                        ModifiedTime = await File.GetModifiedTimeAsync();
                        Size = await File.GetSizeRawDataAsync();
                    }
                }
                catch (Exception ex)
                {
                    LogTracer.Log(ex, $"An unexpected exception was threw in {nameof(LoadCoreAsync)}");
                }
            }
        }

        public async override Task<IStorageItem> GetStorageItemAsync()
        {
            try
            {
                return StorageItem ??= await StorageFile.GetFileFromPathAsync(Path);
            }
            catch (FileNotFoundException)
            {
                LogTracer.Log($"Could not get StorageFile because file is not found, path: {Path}");
            }
            catch (UnauthorizedAccessException)
            {
                LogTracer.Log($"Could not get StorageFile because do not have enough permission to access this file, path: {Path}");
            }
            catch (Exception ex)
            {
                LogTracer.Log(ex, $"Could not get StorageFile, path: {Path}");
            }

            return null;
        }

        public static explicit operator StorageFile(FileSystemStorageFile File)
        {
            return File.StorageItem;
        }
    }
}
