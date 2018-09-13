﻿using Amazon.S3.Transfer;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace DriverApp
{
    class S3StreamWriter : Stream, IDisposable
    {
        private byte[] Buffer;

        private int Offset;

        private List<UploadPartResponse> partResponses;

        public AmazonS3Client S3Client;

        public string UploadId { get; private set; }

        public string Bucket { get; private set; }

        public string Key { get; private set; }

        public int Part { get; private set; }

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => Buffer.LongLength;

        public override long Position { get => Offset; set => Offset = (int)value; }

        public S3StreamWriter(string bucket, string key) : this(bucket, key, 5242880)
        {
        }

        public S3StreamWriter(string bucket, string key, int size) : base()
        {
            if (size < 5242880)
            {
                size = 5242880;
            }

            Buffer = new byte[size];
            Offset = 0;

            S3Client = new AmazonS3Client();
            Bucket = bucket;
            Key = key;
        }

        public S3StreamWriter(string bucket, string key, AmazonS3Client s3client, int size) : base()
        {
            if (size < 5242880)
            {
                size = 5242880;
            }

            Buffer = new byte[size];
            Offset = 0;

            S3Client = s3client;
            Bucket = bucket;
            Key = key;
        }

        public S3StreamWriter(string bucket, string key, AmazonS3Client s3client) : this(bucket, key, s3client, 5242880)
        {
        }


        private void Start()
        {
            partResponses = new List<UploadPartResponse>();

            var request = new InitiateMultipartUploadRequest
            {
                BucketName = Bucket,
                CannedACL = S3CannedACL.BucketOwnerFullControl,
                Key = Key
            };

            var response = S3Client.InitiateMultipartUpload(request);

            UploadId = response.UploadId;
            Part = 0;
        }

        public override void Flush()
        {
            Console.WriteLine($"Flush {Offset}");

            if (Offset < Buffer.Length)
            {
                return;
            }

            if (string.IsNullOrEmpty(UploadId))
            {
                Start();
            }

            UploadPart();
        }

        private void UploadPart()
        {
            if (Offset == 0)
            {
                return;
            }

            Part += 1;

            Console.WriteLine($"UploadPart {Part} {Offset}");

            using (var stream = new MemoryStream(Buffer, 0, Offset, false))
            {
                var request = new UploadPartRequest()
                {
                    BucketName = Bucket,
                    Key = Key,
                    UploadId = UploadId,
                    PartNumber = Part,
                    PartSize = Offset,
                    InputStream = stream
                };

                var response = S3Client.UploadPart(request);

                partResponses.Add(response);
            }

            Buffer.Initialize();
            Offset = 0;

        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotImplementedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotImplementedException();
        }

        public override void SetLength(long value)
        {
            byte[] buffer = new byte[value];
            Array.Copy(Buffer, buffer, value);
            if (value < Offset)
            {
                Offset = (int)value;
            }

            Buffer = buffer;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            Console.WriteLine($"Write {offset} {count}");

            lock (Buffer)
            {
                if (Offset + count < Buffer.Length)
                {
                    Console.WriteLine($"Write NoFlush {Offset}");
                    Array.Copy(buffer, offset, Buffer, Offset, count);
                    Offset += count;
                }
                else
                {
                    while (count > 0)
                    {
                        int free = Buffer.Length - Offset;
                        if (free > count)
                        {
                            free = count;
                        }

                        Console.WriteLine($"Write Flush {offset} {free} {count}");

                        Array.Copy(buffer, offset, Buffer, Offset, free);
                        Offset += free;
                        offset += free;
                        count -= free;
                        Flush();
                    }
                }
            }
        }

        public override void Close()
        {
            if (!string.IsNullOrEmpty(UploadId))
            {
                Console.WriteLine("Close");

                UploadPart();

                var request = new CompleteMultipartUploadRequest()
                {
                    BucketName = Bucket,
                    Key = Key,
                    UploadId = UploadId,
                    PartETags = null
                };

                request.AddPartETags(partResponses);

                var response = S3Client.CompleteMultipartUpload(request);

                UploadId = null;
                partResponses.Clear();
                Offset = 0;
                Buffer.Initialize();
            }

            base.Close();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Console.WriteLine("Dispose");
                partResponses = null;
                Buffer = null;
            }
        }
    }
}
