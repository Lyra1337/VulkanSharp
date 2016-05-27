﻿using System;
using System.Collections.Generic;
using Vulkan.Android;
using Vulkan;

namespace ClearView
{
	public class ClearView : VulkanView
	{
		Device device;
		Queue queue;
		SwapchainKhr swapchain;
		Semaphore semaphore;
		Fence fence;
		CommandBuffer [] commandBuffers;
		bool initialized = false;

		public ClearView (Android.Content.Context context) : base (context)
		{
		}

		protected override void NativeWindowAcquired ()
		{
			InitializeVulkan ();
		}

		SurfaceFormatKhr SelectFormat (PhysicalDevice physicalDevice, SurfaceKhr surface)
		{
			foreach (var f in physicalDevice.GetSurfaceFormatsKHR (surface))
				if (f.Format == Format.R8g8b8a8Unorm)
					return f;

			throw new Exception ("didn't find the R8g8b8a8Unorm format");
		}

		SwapchainKhr CreateSwapchain (SurfaceKhr surface, SurfaceCapabilitiesKhr surfaceCapabilities, SurfaceFormatKhr surfaceFormat)
		{
			var swapchainInfo = new SwapchainCreateInfoKhr {
				Surface = surface,
				MinImageCount = surfaceCapabilities.MinImageCount,
				ImageFormat = surfaceFormat.Format,
				ImageColorSpace = surfaceFormat.ColorSpace,
				ImageExtent = surfaceCapabilities.CurrentExtent,
				ImageUsage = (uint)ImageUsageFlags.ColorAttachment,
				PreTransform = SurfaceTransformFlagsKhr.Identity,
				ImageArrayLayers = 1,
				ImageSharingMode = SharingMode.Exclusive,
				QueueFamilyIndices = new uint [] { 0 },
				PresentMode = PresentModeKhr.Fifo,
				CompositeAlpha = CompositeAlphaFlagsKhr.Inherit
			};
			return device.CreateSwapchainKHR (swapchainInfo, null);
		}

		Framebuffer [] CreateFramebuffers (Image[] images, SurfaceFormatKhr surfaceFormat, SurfaceCapabilitiesKhr surfaceCapabilities, RenderPass renderPass)
		{
			var displayViews = new ImageView [images.Length];
			for (int i = 0; i < images.Length; i++) {
				var viewCreateInfo = new ImageViewCreateInfo () {
					Image = images [i],
					ViewType = ImageViewType.View2D,
					Format = surfaceFormat.Format,
					Components = new ComponentMapping () {
						R = ComponentSwizzle.R,
						G = ComponentSwizzle.G,
						B = ComponentSwizzle.B,
						A = ComponentSwizzle.A
					},
					SubresourceRange = new ImageSubresourceRange () {
						AspectMask = (uint)ImageAspectFlags.Color,
						LevelCount = 1,
						LayerCount = 1
					}
				};
				displayViews [i] = device.CreateImageView (viewCreateInfo, null);
			}
			var framebuffers = new Framebuffer [images.Length];
			for (int i = 0; i < images.Length; i++) {
				var frameBufferCreateInfo = new FramebufferCreateInfo () {
					Layers = 1,
					RenderPass = renderPass,
					Attachments = new ImageView [] { displayViews [i] },
					Width = surfaceCapabilities.CurrentExtent.Width,
					Height = surfaceCapabilities.CurrentExtent.Height
				};
				framebuffers [i] = device.CreateFramebuffer (frameBufferCreateInfo, null);
			}
			return framebuffers;
		}

		CommandBuffer[] CreateCommandBuffers (Image[] images, Framebuffer[] framebuffers, RenderPass renderPass, SurfaceCapabilitiesKhr surfaceCapabilities)
		{
			var createPoolInfo = new CommandPoolCreateInfo () { Flags = (uint)CommandPoolCreateFlags.ResetCommandBuffer };
			var commandPool = device.CreateCommandPool (createPoolInfo, null);
			var buffers = new CommandBuffer [images.Length];
			for (int i = 0; i < images.Length; i++) {
				var commandBufferAllocateInfo = new CommandBufferAllocateInfo () {
					Level = CommandBufferLevel.Primary,
					CommandPool = commandPool,
					CommandBufferCount = 1
				};
				buffers [i] = device.AllocateCommandBuffers (commandBufferAllocateInfo);

				var commandBufferBeginInfo = new CommandBufferBeginInfo ();
				buffers [i].Begin (commandBufferBeginInfo);
				var renderPassBeginInfo = new RenderPassBeginInfo () {
					Framebuffer = framebuffers [i],
					RenderPass = renderPass,
					ClearValues = new ClearValue [] { new ClearValue () { Color = new ClearColorValue (new float [4] { 0.9f, 0.7f, 0.0f, 1.0f }) } },
					RenderArea = new Rect2D () { Extent = surfaceCapabilities.CurrentExtent }
				};
				buffers [i].CmdBeginRenderPass (renderPassBeginInfo, SubpassContents.Inline);
				buffers [i].CmdEndRenderPass ();
				buffers [i].End ();
			}
			return buffers;
		}

		RenderPass CreateRenderPass (SurfaceFormatKhr surfaceFormat)
		{
			var attDesc = new AttachmentDescription () {
				Format = surfaceFormat.Format,
				Samples = (uint)SampleCountFlags.Count1,
				LoadOp = AttachmentLoadOp.Clear,
				StoreOp = AttachmentStoreOp.Store,
				StencilLoadOp = AttachmentLoadOp.DontCare,
				StencilStoreOp = AttachmentStoreOp.DontCare,
				InitialLayout = ImageLayout.ColorAttachmentOptimal,
				FinalLayout = ImageLayout.ColorAttachmentOptimal
			};
			var attRef = new AttachmentReference () { Layout = ImageLayout.ColorAttachmentOptimal };
			var subpassDesc = new SubpassDescription () {
				PipelineBindPoint = PipelineBindPoint.Graphics,
				ColorAttachments = new AttachmentReference [] { attRef },
			};
			var renderPassCreateInfo = new RenderPassCreateInfo () {
				Attachments = new AttachmentDescription [] { attDesc },
				Subpasses = new SubpassDescription [] { subpassDesc },
			};
			return device.CreateRenderPass (renderPassCreateInfo, null);
		}

		public void InitializeVulkan ()
		{
			var devices = Instance.EnumeratePhysicalDevices ();
			var surface = Instance.CreateAndroidSurfaceKHR (new AndroidSurfaceCreateInfoKhr () { Window = aNativeWindow }, null);
			var queueInfo = new DeviceQueueCreateInfo { QueuePriorities = new float [] { 1.0f } };
			var deviceInfo = new DeviceCreateInfo {
				EnabledExtensionNames = new string [] { "VK_KHR_swapchain", "VK_KHR_display_swapchain" },
				QueueCreateInfos = new DeviceQueueCreateInfo [] { queueInfo }
			};
			var physicalDevice = devices [0];
			device = physicalDevice.CreateDevice (deviceInfo, null);
			queue = device.GetQueue (0, 0);
			var surfaceCapabilities = physicalDevice.GetSurfaceCapabilitiesKHR (surface);
			var surfaceFormat = SelectFormat (physicalDevice, surface);
			swapchain = CreateSwapchain (surface, surfaceCapabilities, surfaceFormat);
			var images = device.GetSwapchainImagesKHR (swapchain);
			var renderPass = CreateRenderPass (surfaceFormat);
			var framebuffers = CreateFramebuffers (images, surfaceFormat, surfaceCapabilities, renderPass);
			commandBuffers = CreateCommandBuffers (images, framebuffers, renderPass, surfaceCapabilities);
			var fenceInfo = new FenceCreateInfo ();
			fence = device.CreateFence (fenceInfo, null);
			var semaphoreInfo = new SemaphoreCreateInfo ();
			semaphore = device.CreateSemaphore (semaphoreInfo, null);
			initialized = true;
		}

		protected override void OnDraw (global::Android.Graphics.Canvas canvas)
		{
			if (initialized)
				DrawFrame ();
		}

		void DrawFrame ()
		{
			uint nextIndex = device.AcquireNextImageKHR (swapchain, UInt64.MaxValue, semaphore, fence);
			device.ResetFences (1, fence);
			var submitInfo = new SubmitInfo () {
				WaitSemaphores = new Semaphore [] { semaphore },
				CommandBuffers = new CommandBuffer [] { commandBuffers [nextIndex] }
			};
			queue.Submit (1, submitInfo, fence);
			device.WaitForFences (1, fence, true, 100000000);
			var presentInfo = new PresentInfoKhr () {
				Swapchains = new SwapchainKhr [] { swapchain },
				ImageIndices = new uint [] { nextIndex },
			};
			queue.PresentKHR (presentInfo);
		}
	}
}
