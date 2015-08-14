﻿using img = ImageProcessor;
using ImageProcessor.Imaging;
using ImageProcessor.Imaging.Formats;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using Wyam.Common;
using Wyam.Core.Helpers;

namespace Wyam.Modules.ImageProcessor
{
    public class ImageProcessor : IModule
    {
        List<ImageInstruction> _instructions;

        ImageInstruction _currentInstruction;

        public ImageProcessor()
        {
            _instructions = new List<ImageInstruction>();
        }

        void EnsureCurrentInstruction()
        {
            if (_currentInstruction == null)
            {
                _currentInstruction = new ImageInstruction();
                _instructions.Add(_currentInstruction);
            }
        }

        public ImageProcessor Resize(int? width, int? height, AnchorPosition anchor = AnchorPosition.Center)
        {
            EnsureCurrentInstruction();

            _currentInstruction.Width = width;
            _currentInstruction.Height = height;
            _currentInstruction.AnchorPosition = anchor;

            return this;
        }

        public ImageProcessor Constrain(int width, int height)
        {
            EnsureCurrentInstruction();

            _currentInstruction.Constraint = new Size(width, height);

            return this;
        }

        public ImageProcessor ApplyFilters(params ImageFilter[] filters)
        {
            EnsureCurrentInstruction();

            _currentInstruction.Filters.AddRange(filters);

            return this;
        }

        public ImageProcessor Brighten(short percentage)
        {
            if (percentage < 0 && percentage > 100)
                throw new ArgumentException($"Percentage must be between 0 and 100 instead of {percentage}%");

            EnsureCurrentInstruction();

            _currentInstruction.Brightness = percentage;

            return this;
        }

        public ImageProcessor Darken(short percentage)
        {
            if (percentage < 0 && percentage > 100)
                throw new ArgumentException($"Percentage must be between 0 and 100 instead of {percentage}%");

            EnsureCurrentInstruction();

            _currentInstruction.Brightness = -percentage;

            return this;
        }

        public ImageProcessor SetOpacity(short percentage)
        {
            if (percentage < 0 && percentage > 100)
                throw new ArgumentException($"Percentage must be between 0 and 100 instead of {percentage}%");

            EnsureCurrentInstruction();

            _currentInstruction.Opacity = percentage;

            return this;
        }

        public ImageProcessor SetHue(short degrees, bool rotate = false)
        {
            if (degrees < 0 && degrees > 360)
                throw new ArgumentException($"Degrees must be between 0 and 360 instead of {degrees}");

            EnsureCurrentInstruction();

            _currentInstruction.Hue = new HueInstruction
            {
                Degrees = degrees,
                Rotate = rotate
            };

            return this;
        }

        public ImageProcessor Tint (Color color)
        {
            EnsureCurrentInstruction();
            _currentInstruction.Tint = color;

            return this;
        }

        public ImageProcessor Vignette(Color color)
        {
            EnsureCurrentInstruction();
            _currentInstruction.Vignette = color;

            return this;
        }

        public ImageProcessor Saturate(short percentage)
        {
            if (percentage < 0 && percentage > 100)
                throw new ArgumentException($"Percentage must be between 0 and 100 instead of {percentage}%");

            EnsureCurrentInstruction();
            _currentInstruction.Saturation = percentage;
            return this;
        }

        public ImageProcessor Desaturate(short percentage)
        {
            if (percentage < 0 && percentage > 100)
                throw new ArgumentException($"Percentage must be between 0 and 100 instead of {percentage}%");

            EnsureCurrentInstruction();
            _currentInstruction.Saturation = -percentage;
            return this;
        }


        public ImageProcessor SetJpegQuality(short quality)
        {
            if (quality < 0 && quality > 100)
                throw new ArgumentException($"Quality must be between 0 and 100 instead of {quality}");

            EnsureCurrentInstruction();
            _currentInstruction.JpegQuality = quality;
            return this;
        }

        public ImageProcessor SetContrast(short percentage)
        {
            if (percentage < -100 && percentage > 100)
                throw new ArgumentException($"Percentage must be between -100 and 100 instead of {percentage}%");

            EnsureCurrentInstruction();
            _currentInstruction.Contrast = percentage;
            return this;
        }

        public ImageProcessor And
        {
            get
            {
                _currentInstruction = null;
                return this;
            }
        }

        ISupportedImageFormat GetFormat(string extension, ImageInstruction ins)
        {
            ISupportedImageFormat format = null;

            if (extension == ".jpg" || extension == ".jpeg")
                format = new JpegFormat { Quality = ins.JpegQuality };
            else
                if (extension == ".gif")
                format = new GifFormat { };
            else if (extension == ".png")
                format = new PngFormat { };

            return format;
        }

        public IEnumerable<IDocument> Execute(IReadOnlyList<IDocument> inputs, IExecutionContext context)
        {
            foreach (IDocument input in inputs)
            {
                var path = input.Get<string>(MetadataKeys.SourceFilePath);

                if (string.IsNullOrWhiteSpace(path))
                    continue;

                var relativePath = Path.GetDirectoryName(PathHelper.GetRelativePath(context.InputFolder, path));
                string destination = Path.Combine(context.OutputFolder, relativePath, Path.GetFileName(path));

                string destinationDirectory = Path.GetDirectoryName(destination);
                if (!Directory.Exists(destinationDirectory))
                {
                    Directory.CreateDirectory(destinationDirectory);
                }

                var extension = Path.GetExtension(path);

                
                foreach (var ins in _instructions)
                {
                    ISupportedImageFormat format = GetFormat(extension, ins);
                    if (format == null)
                        continue;

                    if (input.Stream.CanSeek)
                    {
                        input.Stream.Seek(0, SeekOrigin.Begin);
                    }

                    string destinationFile = Path.GetFileNameWithoutExtension(path);
                    destinationFile += ins.GetSuffix() + extension;

                    var destinationPath = Path.Combine(destinationDirectory, destinationFile);
                    context.Trace.Verbose($"WritePath: {destinationPath}");

                    var output = ProcessImage(input.Stream, format, ins);

                    var clone = input.Clone(output, new Dictionary<string, object>
                        {
                            {Wyam.Core.Documents.MetadataKeys.WritePath, destinationPath},
                            {Wyam.Core.Documents.MetadataKeys.WriteExtension, extension }
                        });

                    yield return clone;
                }
            }
        }

        Stream ProcessImage(Stream inStream, ISupportedImageFormat format, ImageInstruction ins)
        {
            using (var imageFactory = new img.ImageFactory(preserveExifData: true))
            {
                // Load, resize, set the format and quality and save an image.
                var fac = imageFactory.Load(inStream)
                            .Format(format);

                if (ins.IsNeedResize)
                {
                    if (ins.IsCropRequired)
                    {
                        var layer = new ResizeLayer(
                            size: ins.GetCropSize().Value,
                            anchorPosition: ins.GetAnchorPosition(),
                            resizeMode: ResizeMode.Crop
                            );

                        fac.Resize(layer);
                    }
                    else
                    {
                        fac.Resize(ins.GetCropSize().Value);
                    }
                }

                foreach (var f in ins.Filters)
                {
                    fac.Filter(ins.GetMatrixFilter(f));
                }

                if (ins.Brightness.HasValue)
                {
                    fac.Brightness(ins.Brightness.Value);
                }

                if (ins.Constraint.HasValue)
                {
                    fac.Constrain(ins.Constraint.Value);
                }

                if (ins.Opacity.HasValue)
                {
                    fac.Alpha(ins.Opacity.Value);
                }

                if (ins.Hue != null)
                {
                    fac.Hue(ins.Hue.Degrees, ins.Hue.Rotate);
                }

                if (ins.Tint != null)
                {
                    fac.Tint(ins.Tint.Value);
                }

                if (ins.Vignette != null)
                {
                    fac.Vignette(ins.Vignette.Value);
                }

                if (ins.Saturation.HasValue)
                {
                    fac.Saturation(ins.Saturation.Value);
                }

                if (ins.Contrast.HasValue)
                {
                    fac.Contrast(ins.Contrast.Value);
                }

                var outputStream = new MemoryStream();
                fac.Save(outputStream);
                outputStream.Seek(0, SeekOrigin.Begin);
                return outputStream;
            }
        }
    }
}
