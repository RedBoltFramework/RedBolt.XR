// Copyright (c) 2026 RedBolt and contributors.
// This work is licensed under the terms of the MIT license.

namespace RedBolt.XR;

using System;

using RedBolt.Bindings.Diligent;
using RedBolt.Bindings.OpenXR;
using RedBolt.Graphics;
using RedBolt.Math;
using RedBolt.RenderAPI;

using XrApi = RedBolt.Bindings.OpenXR.OpenXR;

internal sealed class OpenXRSwapChain : IDisposable
{
    private const ulong ColorAttachmentUsage = 0x0000_0001UL;
    private const ulong DepthStencilAttachmentUsage = 0x0000_0002UL;
    private const ulong SampledUsage = 0x0000_0020UL;

    private readonly Texture2D[] _textures;
    private readonly OpenXROwnedHandle<XrSwapchainT> _swapchain;
    private IDataBlob? _imageData;

    private OpenXRSwapChain(OpenXROwnedHandle<XrSwapchainT> swapchain, Texture2D[] textures,
        IDataBlob imageData, TextureFormat format)
    {
        _swapchain = swapchain;
        _textures = textures;
        _imageData = imageData;
        Format = format;
    }

    public TextureFormat Format { get; }

    public XrSwapchainT Handle => _swapchain.Value!;

    public static OpenXRSwapChain? Create(XrSessionT session, RenderDevice renderDevice, TextureFormat format,
        long internalFormat, Vector2i eyeSize, int multiSample)
    {
        var isDepth = RenderAPIUtils.IsDepthTextureFormat(format);
        var width = checked(eyeSize.X * 2);
        var height = eyeSize.Y;
        using var swapchainInfo = new XrSwapchainCreateInfo
        {
            Type = XrStructureType.XrTypeSwapchainCreateInfo,
            UsageFlags = SampledUsage | (isDepth ? DepthStencilAttachmentUsage : ColorAttachmentUsage),
            Format = internalFormat,
            Width = checked((uint)width),
            Height = checked((uint)height),
            SampleCount = checked((uint)multiSample),
            FaceCount = 1,
            ArraySize = 1,
            MipCount = 1
        };
        if (!OpenXRPrimitives.Check(XrApi.XrCreateSwapchain(session, swapchainInfo, out var swapchain),
                nameof(XrApi.XrCreateSwapchain)))
            return null;

        var ownedSwapchain = new OpenXROwnedHandle<XrSwapchainT>(swapchain, XrApi.XrDestroySwapchain);
        if (!OpenXRPrimitives.Check(XrApi.XrEnumerateSwapchainImages(swapchain, 0, out var imageCount, null!),
                nameof(XrApi.XrEnumerateSwapchainImages)) || imageCount == 0)
        {
            ownedSwapchain.Dispose();
            return null;
        }

        var nativeDevice = renderDevice.GetRenderDevice()!;
        var nativeDeviceType = renderDevice.GetBackend() switch
        {
            RenderBackend.D3D11 => RenderDeviceType.RenderDeviceTypeD3d11,
            RenderBackend.D3D12 => RenderDeviceType.RenderDeviceTypeD3d12,
            RenderBackend.Vulkan => RenderDeviceType.RenderDeviceTypeVulkan,
#if ANDROID
            RenderBackend.OpenGL => RenderDeviceType.RenderDeviceTypeGles,
#else
            RenderBackend.OpenGL => RenderDeviceType.RenderDeviceTypeGl,
#endif
            _ => RenderDeviceType.RenderDeviceTypeUndefined
        };

        Diligent.AllocateOpenXRSwapchainImageData(nativeDeviceType, imageCount, out var imageData);
        var imageHeader = XrSwapchainImageBaseHeader.CreateManagedWrapper(imageData.GetDataPtr(), false);
        if (!OpenXRPrimitives.Check(
                XrApi.XrEnumerateSwapchainImages(swapchain, imageCount, out imageCount, imageHeader),
                nameof(XrApi.XrEnumerateSwapchainImages)))
        {
            imageHeader.Dispose();
            _ = imageData.Release();
            imageData.Dispose();
            ownedSwapchain.Dispose();
            return null;
        }

        imageHeader.Dispose();
        var textures = new Texture2D[checked((int)imageCount)];
        using var textureDesc = new TextureDesc
        {
            Type = ResourceDimension.ResourceDimTex2d,
            Width = checked((uint)width),
            Height = checked((uint)height),
            ArraySize = 1,
            Format = format,
            MipLevels = 1,
            SampleCount = checked((uint)multiSample),
            BindFlags = BindFlags.BindShaderResource |
                (isDepth ? BindFlags.BindDepthStencil : BindFlags.BindRenderTarget)
        };
        for (uint index = 0; index < imageCount; ++index)
        {
            Diligent.GetOpenXRSwapchainImage(nativeDevice, imageData.GetDataPtr(), index, textureDesc,
                out var nativeTexture);
            if (nativeTexture is null || nativeTexture.IsNull)
            {
                foreach (var allocatedTexture in textures)
                    allocatedTexture?.Dispose();
                _ = imageData.Release();
                imageData.Dispose();
                ownedSwapchain.Dispose();
                return null;
            }

            var texture = new Texture2D();
            if (!texture.GetRawTexture().CreateFromHandle(nativeTexture, format, multiSample))
            {
                texture.Dispose();
                foreach (var previousTexture in textures)
                    previousTexture?.Dispose();
                _ = imageData.Release();
                imageData.Dispose();
                ownedSwapchain.Dispose();
                return null;
            }
            textures[index] = texture;
        }

        return new OpenXRSwapChain(ownedSwapchain, textures, imageData, format);
    }

    public Texture2D? AcquireImage()
    {
        using var acquireInfo = new XrSwapchainImageAcquireInfo
        {
            Type = XrStructureType.XrTypeSwapchainImageAcquireInfo
        };
        if (!OpenXRPrimitives.Check(XrApi.XrAcquireSwapchainImage(Handle, acquireInfo, out var textureIndex),
                nameof(XrApi.XrAcquireSwapchainImage)))
            return null;

        using var waitInfo = new XrSwapchainImageWaitInfo
        {
            Type = XrStructureType.XrTypeSwapchainImageWaitInfo,
            Timeout = OpenXRPrimitives.InfiniteDuration
        };
        return OpenXRPrimitives.Check(XrApi.XrWaitSwapchainImage(Handle, waitInfo),
            nameof(XrApi.XrWaitSwapchainImage)) ? _textures[textureIndex] : null;
    }

    public void ReleaseImage()
    {
        using var releaseInfo = new XrSwapchainImageReleaseInfo
        {
            Type = XrStructureType.XrTypeSwapchainImageReleaseInfo
        };
        _ = OpenXRPrimitives.Check(XrApi.XrReleaseSwapchainImage(Handle, releaseInfo),
            nameof(XrApi.XrReleaseSwapchainImage));
    }

    public void Dispose()
    {
        foreach (var texture in _textures)
            texture.Dispose();
        if (_imageData is not null)
        {
            _ = _imageData.Release();
            _imageData.Dispose();
            _imageData = null;
        }
        _swapchain.Dispose();
    }
}
