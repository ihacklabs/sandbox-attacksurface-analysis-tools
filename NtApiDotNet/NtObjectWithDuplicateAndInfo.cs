﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace NtApiDotNet
{
    /// <summary>
    /// A derived class to add some useful functions such as Duplicate as well as generic Query and Set information methods.
    /// </summary>
    /// <typeparam name="O">The derived type to use as return values</typeparam>
    /// <typeparam name="A">An enum which represents the access mask values for the type</typeparam>
    /// <typeparam name="I">An enum which represents the information class for query/set.</typeparam>
    public abstract class NtObjectWithDuplicateAndInfo<O, A, I> : NtObjectWithDuplicate<O, A> where O : NtObject where A : struct, IConvertible where I : struct
    {
        #region Constructors
        internal NtObjectWithDuplicateAndInfo(SafeKernelObjectHandle handle) : base(handle)
        {
            System.Diagnostics.Debug.Assert(typeof(I).IsEnum);
        }
        #endregion

        #region Public Methods

        /// <summary>
        /// Query a fixed structure from the object.
        /// </summary>
        /// <typeparam name="T">The type of structure to return.</typeparam>
        /// <param name="info_class">The information class to query.</param>
        /// <param name="default_value">A default value for the query.</param>
        /// <param name="throw_on_error">True to throw on error.</param>
        /// <returns>The result of the query.</returns>
        /// <exception cref="NtException">Thrown on error.</exception>
        public virtual NtResult<T> Query<T>(I info_class, T default_value, bool throw_on_error) where T : new()
        {
            using (var buffer = new SafeStructureInOutBuffer<T>(default_value))
            {
                return QueryInformation(info_class, buffer, out int return_length).CreateResult(throw_on_error, () => buffer.Result);
            }
        }

        /// <summary>
        /// Query a fixed structure from the object.
        /// </summary>
        /// <typeparam name="T">The type of structure to return.</typeparam>
        /// <param name="info_class">The information class to query.</param>
        /// <param name="default_value">A default value for the query.</param>
        /// <returns>The result of the query.</returns>
        /// <exception cref="NtException">Thrown on error.</exception>
        public T Query<T>(I info_class, T default_value) where T : new()
        {
            return Query(info_class, default_value, true).Result;
        }

        /// <summary>
        /// Query a fixed structure from the object.
        /// </summary>
        /// <typeparam name="T">The type of structure to return.</typeparam>
        /// <param name="info_class">The information class to query.</param>
        /// <returns>The result of the query.</returns>
        /// <exception cref="NtException">Thrown on error.</exception>
        public T Query<T>(I info_class) where T : new()
        {
            return Query(info_class, new T());
        }

        /// <summary>
        /// Query a variable buffer from the object.
        /// </summary>
        /// <typeparam name="T">The type of structure to return.</typeparam>
        /// <param name="info_class">The information class to query.</param>
        /// <param name="default_value">A default value for the query.</param>
        /// <param name="throw_on_error">True to throw on error.</param>
        /// <returns>The result of the query.</returns>
        /// <exception cref="NtException">Thrown on error.</exception>
        public virtual NtResult<SafeStructureInOutBuffer<T>> QueryBuffer<T>(I info_class, T default_value, bool throw_on_error) where T : new()
        {
            NtStatus status;
            int return_length;
            // First try base size before trying to reallocate.
            using (var buffer = default_value.ToBuffer())
            {
                status = QueryInformation(info_class, buffer, out return_length);
                if (status.IsSuccess())
                {
                    return status.CreateResult(false, () => buffer.Detach());
                }
            }

            if (!IsInvalidBufferSize(status))
            {
                return status.CreateResultFromError<SafeStructureInOutBuffer<T>>(throw_on_error);
            }

            // If the function returned a length then trust it.
            if (return_length > 0)
            {
                using (var buffer = new SafeStructureInOutBuffer<T>(default_value, return_length, false))
                {
                    return QueryInformation(info_class, buffer, out return_length).CreateResult(throw_on_error, () => buffer.Detach());
                }
            }

            // Function length can't be trusted, we'll need to brute force it.
            return_length = GetSmallestPower2(Marshal.SizeOf(typeof(T)));
            int max_length = GetMaximumBruteForceLength();
            while (return_length < max_length)
            {
                using (var buffer = new SafeStructureInOutBuffer<T>(default_value, return_length, false))
                {
                    status = QueryInformation(info_class, buffer, out int dummy_length);
                    if (status.IsSuccess())
                    {
                        return status.CreateResult(throw_on_error, () => buffer.Detach());
                    }
                    else if (!IsInvalidBufferSize(status))
                    {
                        return status.CreateResultFromError<SafeStructureInOutBuffer<T>>(throw_on_error);
                    }
                    return_length *= 2;
                }
            }

            return NtStatus.STATUS_BUFFER_TOO_SMALL.CreateResultFromError<SafeStructureInOutBuffer<T>>(throw_on_error);
        }

        /// <summary>
        /// Query a variable buffer from the object.
        /// </summary>
        /// <typeparam name="T">The type of structure to return.</typeparam>
        /// <param name="info_class">The information class to query.</param>
        /// <param name="default_value">A default value for the query.</param>
        /// <returns>The result of the query.</returns>
        /// <exception cref="NtException">Thrown on error.</exception>
        public virtual SafeStructureInOutBuffer<T> QueryBuffer<T>(I info_class, T default_value) where T : new()
        {
            return QueryBuffer(info_class, default_value, true).Result;
        }

        /// <summary>
        /// Query a variable buffer from the object.
        /// </summary>
        /// <typeparam name="T">The type of structure to return.</typeparam>
        /// <param name="info_class">The information class to query.</param>
        /// <returns>The result of the query.</returns>
        /// <exception cref="NtException">Thrown on error.</exception>
        public SafeStructureInOutBuffer<T> QueryBuffer<T>(I info_class) where T : new()
        {
            return QueryBuffer(info_class, new T(), true).Result;
        }

        /// <summary>
        /// Set a value to the object.
        /// </summary>
        /// <typeparam name="T">The type of structure to set.</typeparam>
        /// <param name="info_class">The information class to set.</param>
        /// <param name="value">The value to set.</param>
        /// <param name="throw_on_error">True to throw on error.</param>
        /// <returns>The NT status code of the set.</returns>
        /// <exception cref="NtException">Thrown on error.</exception>
        public virtual NtStatus Set<T>(I info_class, T value, bool throw_on_error) where T : new()
        {
            using (var buffer = value.ToBuffer())
            {
                return SetInformation(info_class, buffer).ToNtException(throw_on_error);
            }
        }

        /// <summary>
        /// Set a value to the object.
        /// </summary>
        /// <typeparam name="T">The type of structure to set.</typeparam>
        /// <param name="info_class">The information class to set.</param>
        /// <param name="value">The value to set.</param>
        /// <returns>The NT status code of the set.</returns>
        /// <exception cref="NtException">Thrown on error.</exception>
        public void Set<T>(I info_class, T value) where T : new()
        {
            Set(info_class, value, true);
        }

        /// <summary>
        /// Method to query information for this object type.
        /// </summary>
        /// <param name="info_class">The information class.</param>
        /// <param name="buffer">The buffer to return data in.</param>
        /// <param name="return_length">Return length from the query.</param>
        /// <returns>The NT status code for the query.</returns>
        public virtual NtStatus QueryInformation(I info_class, SafeBuffer buffer, out int return_length)
        {
            return_length = 0;
            return NtStatus.STATUS_NOT_SUPPORTED;
        }

        /// <summary>
        /// Method to set information for this object type.
        /// </summary>
        /// <param name="info_class">The information class.</param>
        /// <param name="buffer">The buffer to set data from.</param>
        /// <returns>The NT status code for the set.</returns>
        public virtual NtStatus SetInformation(I info_class, SafeBuffer buffer)
        {
            return NtStatus.STATUS_NOT_SUPPORTED;
        }

        #endregion

        #region Protected Methods
        /// <summary>
        /// Overriddable method to determine the maximum brute force length for query.
        /// </summary>
        /// <returns>The maximum bytes to brute force. Returning 0 will disable brute force.</returns>
        protected virtual int GetMaximumBruteForceLength()
        {
            return 16 * 1024;
        }
        #endregion

        #region Private Members
        private static bool IsInvalidBufferSize(NtStatus status)
        {
            return status == NtStatus.STATUS_INFO_LENGTH_MISMATCH || status == NtStatus.STATUS_BUFFER_TOO_SMALL || status == NtStatus.STATUS_BUFFER_OVERFLOW;
        }

        private static int GetSmallestPower2(int size)
        {
            if (size <= 0)
            {
                throw new ArgumentException("Size must be greater than 0");
            }

            // Already a power of 2.
            if ((size & (size - 1)) == 0)
            {
                return size;
            }

            int bits = 0;
            int curr_size = size;
            while (curr_size != 0)
            {
                bits++;
                curr_size >>= 1;
            }

            curr_size = 1 << bits;
            return Math.Max(curr_size, size);
        }

        #endregion
    }
}
